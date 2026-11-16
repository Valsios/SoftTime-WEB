using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Closing;

namespace SoftTime.Infrastructure.Services.Payroll;

/// <summary>
/// Variables envoyées à la paie, par base SAGE (T_PAIE_VARIABLE), et destinations d'une autre base (T_PAIE_ECRITURE).
/// Le calcul n'en dépend jamais : seuls les writers de paie et le Contrôle avant paie lisent la sélection. Les
/// modifications passent par EF, donc par le journal central (qui, quand, avant, après).
/// </summary>
public sealed class PayrollVariableService : IPayrollVariableService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;
    private readonly ICurrentUser _user;
    private readonly IPayrollSendLog _sendLog;
    private readonly ICodeConstanteService _codes;
    private readonly IExternalDiscoveryService _discovery;
    private readonly PayrollVariableCache _cache;
    private readonly ClosingAnalysisCache _closingCache;
    private readonly IClosingChecklistCache _checklistCache;
    private readonly TimeProvider _horloge;
    private readonly ILogger<PayrollVariableService> _logger;
    private readonly IPayrollTargetSchema? _schema;

    public PayrollVariableService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IDatabaseConfigurationAuthorizer configAuth,
        ICurrentUser user,
        IPayrollSendLog sendLog,
        ICodeConstanteService codes,
        IExternalDiscoveryService discovery,
        PayrollVariableCache cache,
        ClosingAnalysisCache closingCache,
        IClosingChecklistCache checklistCache,
        TimeProvider horloge,
        ILogger<PayrollVariableService> logger,
        IPayrollTargetSchema? schema = null)
    {
        _uow = uow;
        _tenant = tenant;
        _configAuth = configAuth;
        _user = user;
        _sendLog = sendLog;
        _codes = codes;
        _discovery = discovery;
        _cache = cache;
        _closingCache = closingCache;
        _checklistCache = checklistCache;
        _horloge = horloge;
        _logger = logger;
        _schema = schema;
    }

    public async Task<PayrollVariableSelection> GetSelectionAsync(string sageDb, CancellationToken ct = default)
    {
        if (_cache.TryGet(sageDb, out var selection))
            return selection;
        selection = Selection(await LireAsync(sageDb, ct));
        _cache.Set(sageDb, selection);
        return selection;
    }

    public async Task<PayrollVariablesActiveDto> GetActivesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var selection = await GetSelectionAsync(_tenant.SageDb, ct);
        // Variables réellement écrites : une variable cochée mais pas encore envoyable (Absence) reste grisée.
        return new PayrollVariablesActiveDto(_tenant.SageDb, selection.Envoyees);
    }

    public async Task<PayrollVariablesConfigDto> GetConfigurationAsync(CancellationToken ct = default)
    {
        var database = await AutoriserAsync(ct);
        return await ConstruireAsync(database, await LireAsync(database.NomBd!, ct), ct);
    }

    public async Task<PayrollVariablesConfigDto> SaveAsync(IReadOnlyList<PayrollVariableSaveDto> variables, CancellationToken ct = default)
    {
        var database = await AutoriserAsync(ct);
        var sageDb = database.NomBd!;
        var baseSage = !EstAutre(database);
        // Autre base : la colonne d'une variable envoyable est sa destination dans T_PAIE_ECRITURE (écran des destinations).
        var cibles = baseSage ? new Dictionary<string, string>() : await CiblesAutreAsync(sageDb, ct);
        var lignes = (variables ?? Array.Empty<PayrollVariableSaveDto>()).Select(v => Reglage(v, baseSage, cibles)).ToList();
        var erreurs = PayrollVariableRules.Valider(lignes, baseSage).ToList();
        if (baseSage)
            lignes = await VerifierCodesSageAsync(lignes, erreurs, ct);
        else
            erreurs.AddRange(await ValiderColonnesAsync(database, lignes, ct));
        if (erreurs.Count > 0)
            throw new DomainException(string.Join(" ", erreurs));

        await RefuserSiMoisEnvoyeAsync(sageDb, ct);

        var repo = _uow.Repository<PaieVariable>();
        var existantes = await repo.ListAsync(v => v.BddSage == sageDb, ct);
        var maintenant = _horloge.GetUtcNow().UtcDateTime;
        foreach (var ligne in lignes)
        {
            var definition = PayrollVariableCatalog.Definition(ligne.Variable);
            var nom = definition.Id;
            var propre = !definition.CodeCommun;
            var code = propre && baseSage ? ligne.CodePaie : null;
            // Colonne saisie conservée seulement pour une variable non envoyable (Absence, comme au lot B).
            var colonne = !definition.Envoyable && !baseSage ? ligne.ColonneCible : null;
            var unite = propre ? ligne.Unite : null;
            var entite = existantes.FirstOrDefault(v => string.Equals(v.Variable, nom, StringComparison.OrdinalIgnoreCase));
            if (entite is null)
            {
                await repo.AddAsync(new PaieVariable
                {
                    BddSage = sageDb, Variable = nom, Actif = ligne.Actif, CodePaie = code, Unite = unite, ColCible = colonne,
                    ModifieLe = maintenant, ModifiePar = _user.Login
                }, ct);
                continue;
            }
            if (entite.Actif == ligne.Actif && entite.CodePaie == code && entite.Unite == unite && entite.ColCible == colonne)
                continue;
            entite.Actif = ligne.Actif;
            entite.CodePaie = code;
            entite.Unite = unite;
            entite.ColCible = colonne;
            entite.ModifieLe = maintenant;
            entite.ModifiePar = _user.Login;
            repo.Update(entite);
        }
        await _uow.SaveChangesAsync(ct);

        _cache.Invalider(sageDb);
        _closingCache.Vider();
        _checklistCache.Vider();
        _logger.LogInformation("Variables envoyées à la paie de la base {Base} modifiées par {Login}.", sageDb, _user.Login);
        return await ConstruireAsync(database, await LireAsync(sageDb, ct), ct);
    }

    /// <summary>
    /// Ligne à valider et enregistrer. Variable à code propre : unité unique (Retard : heures) prise par défaut ; base
    /// SAGE : code saisi ; autre base : colonne d'une variable envoyable lue dans T_PAIE_ECRITURE.
    /// </summary>
    private static PayrollVariableSetting Reglage(PayrollVariableSaveDto v, bool baseSage, IReadOnlyDictionary<string, string> cibles)
    {
        var definition = PayrollVariableCatalog.Trouver(v.Variable);
        var unite = Texte(v.Unite)?.ToUpperInvariant();
        if (definition is { CodeCommun: false, Unites.Count: 1 } && unite is null)
            unite = definition.Unites[0];
        var colonne = definition is { CodeCommun: false, Envoyable: true }
            ? (baseSage ? null : cibles.GetValueOrDefault(definition.Id))
            : Texte(v.ColonneCible);
        return new PayrollVariableSetting(v.Variable, v.Actif, baseSage ? Texte(v.CodePaie) : null, unite, colonne);
    }

    /// <summary>
    /// Base SAGE : le code d'une variable envoyable à code propre (Retard) doit exister dans T_CST de la base active ; il
    /// est enregistré tel qu'il y figure. Liste illisible : enregistrement refusé (aucun code non vérifié).
    /// </summary>
    private async Task<List<PayrollVariableSetting>> VerifierCodesSageAsync(
        List<PayrollVariableSetting> lignes, List<string> erreurs, CancellationToken ct)
    {
        static bool AVerifier(PayrollVariableSetting l) =>
            l.CodePaie is not null && PayrollVariableCatalog.Trouver(l.Variable) is { CodeCommun: false, Envoyable: true };
        if (!lignes.Any(AVerifier))
            return lignes;
        IReadOnlyList<SageConstantOptionDto> options;
        try
        {
            options = await _codes.ListSageConstantOptionsAsync(ct) ?? Array.Empty<SageConstantOptionDto>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Codes de paie de la base {Base} illisibles.", _tenant.SageDb);
            throw new DomainException("Les codes de paie de la base SAGE n'ont pas pu être lus : le code choisi ne peut pas être vérifié. Réessayez plus tard.");
        }
        return lignes.Select(ligne =>
        {
            if (!AVerifier(ligne))
                return ligne;
            var option = options.FirstOrDefault(o => Egal(o.Code, ligne.CodePaie));
            if (option is not null)
                return ligne with { CodePaie = option.Code.Trim() };
            erreurs.Add($"Le code de paie {ligne.CodePaie} de la variable {PayrollVariableRules.Libelle(ligne.Variable)} n'existe pas dans la base SAGE {_tenant.SageDb}.");
            return ligne;
        }).ToList();
    }

    public async Task<PayrollWriteTargetsDto> GetDestinationsAsync(CancellationToken ct = default)
    {
        var database = await AutoriserAutreAsync(ct);
        return await DestinationsAsync(database, ct);
    }

    public async Task<IReadOnlyList<PayrollTargetColumnDto>> GetColonnesAsync(string? table, CancellationToken ct = default)
    {
        await AutoriserAutreAsync(ct);
        var nomTable = TableDbo(Texte(table) ?? throw new DomainException("Choisissez la table de destination."));
        return (await Schema().ListColumnsAsync(nomTable, ct)).Select(ColonneDto).ToList();
    }

    public async Task<PayrollWriteTargetsDto> SaveDestinationsAsync(PayrollWriteTargetsSaveDto destinations, CancellationToken ct = default)
    {
        var database = await AutoriserAutreAsync(ct);
        var sageDb = database.NomBd!;
        var table = Texte(destinations?.Table) ?? throw new DomainException("Choisissez la table de destination.");
        var matricule = Texte(destinations?.ColonneMatricule) ?? throw new DomainException("Choisissez la colonne matricule.");
        PayrollWritingConfiguration.ValidateIdentifier(matricule, "La colonne matricule");
        var nomTable = TableDbo(table);
        var schema = Schema();
        if (!(await schema.ListTablesAsync(ct)).Contains(nomTable, StringComparer.OrdinalIgnoreCase))
            throw new DomainException($"La table {table} n'existe pas dans la base de paie.");
        var colonnes = await schema.ListColumnsAsync(nomTable, ct);
        if (!colonnes.Any(c => Egal(c.Nom, matricule)))
            throw new DomainException($"La colonne matricule {matricule} n'existe pas dans la table {table}.");

        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var repo = _uow.Repository<PaieEcriture>();
        var existantes = await repo.ListAsync(c => c.BddSage == sageDb, ct);
        var erreurs = new List<string>();
        var demandes = Demandes(destinations?.Variables, erreurs);
        // Destination finale de chaque variable envoyable : celle demandée, sinon celle déjà enregistrée.
        var finales = PayrollVariableCatalog.Toutes.Where(d => d.Envoyable).ToDictionary(
            d => d.Id,
            d => demandes.TryGetValue(d.Id, out var demandee) ? demandee : existantes.FirstOrDefault(e => Egal(e.Categorie, d.Id))?.ColValeur);
        var retenues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, colonne) in finales)
        {
            if (colonne is null)
                continue;
            var erreur = ErreurColonne(PayrollVariableCatalog.Definition(id), colonne, colonnes, table, out var retenue);
            if (erreur is null)
                retenues[id] = retenue!;
            else
                erreurs.Add(erreur);
        }
        foreach (var doublon in retenues.GroupBy(r => r.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            erreurs.Add($"La colonne {doublon.Key} est choisie pour plusieurs variables ({string.Join(", ", doublon.Select(d => PayrollVariableRules.Libelle(d.Key)))}).");
        foreach (var envoyee in (await GetSelectionAsync(sageDb, ct)).Envoyees.Where(id => finales[id] is null))
            erreurs.Add($"La variable {PayrollVariableRules.Libelle(envoyee)} est envoyée à la paie : choisissez sa colonne de destination.");
        if (erreurs.Count > 0)
            throw new DomainException(string.Join(" ", erreurs));

        await RefuserSiMoisEnvoyeAsync(sageDb, ct);

        foreach (var id in finales.Keys)
        {
            var entite = existantes.FirstOrDefault(e => Egal(e.Categorie, id));
            if (!retenues.TryGetValue(id, out var colonne))
            {
                if (entite is not null)
                    repo.Remove(entite);
                continue;
            }
            if (entite is null)
            {
                await repo.AddAsync(new PaieEcriture { BddSage = sageDb, TableCible = table, ColMatricule = matricule, Categorie = id, ColValeur = colonne }, ct);
                continue;
            }
            if (entite.TableCible == table && entite.ColMatricule == matricule && entite.ColValeur == colonne && entite.Categorie == id)
                continue;
            entite.TableCible = table;
            entite.ColMatricule = matricule;
            entite.Categorie = id;
            entite.ColValeur = colonne;
            repo.Update(entite);
        }
        await _uow.SaveChangesAsync(ct);

        _cache.Invalider(sageDb);
        _closingCache.Vider();
        _checklistCache.Vider();
        _logger.LogInformation("Destinations de paie de la base {Base} modifiées par {Login}.", sageDb, _user.Login);
        return await DestinationsAsync(database, ct);
    }

    /// <summary>Colonne demandée par variable envoyable ; variable inconnue, non envoyable ou répétée : erreur.</summary>
    private static Dictionary<string, string?> Demandes(IReadOnlyList<PayrollWriteTargetSaveDto>? variables, List<string> erreurs)
    {
        var demandes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var variable in variables ?? Array.Empty<PayrollWriteTargetSaveDto>())
        {
            var definition = PayrollVariableCatalog.Trouver(variable.Variable);
            if (definition is null)
                erreurs.Add($"Variable de paie inconnue : « {variable.Variable} ».");
            else if (!definition.Envoyable)
                erreurs.Add($"La variable {definition.Libelle} ne peut pas encore être envoyée à la paie : son envoi est prévu dans une prochaine étape.");
            else if (!demandes.TryAdd(definition.Id, Texte(variable.Colonne)))
                erreurs.Add($"La variable {definition.Libelle} apparaît plusieurs fois.");
        }
        return demandes;
    }

    /// <summary>La colonne existe, est un identifiant simple, est numérique et accepte les décimales de la variable.</summary>
    private static string? ErreurColonne(
        PayrollVariableDefinition definition, string colonne, IReadOnlyList<PayrollTargetColumn> colonnes, string table, out string? retenue)
    {
        retenue = null;
        var trouvee = colonnes.FirstOrDefault(c => Egal(c.Nom, colonne));
        if (trouvee is null)
            return $"La colonne {colonne} de la variable {definition.Libelle} n'existe pas dans la table {table}.";
        try
        {
            PayrollWritingConfiguration.ValidateIdentifier(trouvee.Nom, $"La colonne de la variable {definition.Libelle}");
        }
        catch (DomainException exception)
        {
            return exception.Message;
        }
        if (!PayrollTargetColumns.EstNumerique(trouvee.Type))
            return $"La colonne {trouvee.Nom} de la variable {definition.Libelle} n'est pas numérique ({trouvee.Type}).";
        var decimales = DecimalesRequises(definition);
        if (!PayrollTargetColumns.AccepteDecimales(trouvee, decimales))
            return $"La colonne {trouvee.Nom} de la variable {definition.Libelle} n'accepte pas {decimales} décimales ({TypeAffiche(trouvee)}) : "
                   + "la valeur est un nombre d'heures avec décimales.";
        retenue = trouvee.Nom;
        return null;
    }

    /// <summary>
    /// Décimales que la colonne doit accepter : deux pour une variable hors heures supplémentaires (Retard, en heures,
    /// decimal(18,2)) ; aucune exigence pour les heures supplémentaires (contrôle historique valeur par valeur à l'écriture).
    /// </summary>
    private static int DecimalesRequises(PayrollVariableDefinition definition) => definition.HeureSupplementaire ? 0 : 2;

    private static string TypeAffiche(PayrollTargetColumn colonne) =>
        colonne.Precision is { } p && colonne.Echelle is { } e && (Egal(colonne.Type, "decimal") || Egal(colonne.Type, "numeric"))
            ? $"{colonne.Type}({p},{e})"
            : colonne.Type;

    private static PayrollTargetColumnDto ColonneDto(PayrollTargetColumn colonne) =>
        new(colonne.Nom, TypeAffiche(colonne), PayrollTargetColumns.EstNumerique(colonne.Type), PayrollTargetColumns.AccepteDecimales(colonne, 2));

    private async Task<PayrollWriteTargetsDto> DestinationsAsync(BddSage database, CancellationToken ct)
    {
        var sageDb = database.NomBd!;
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var lignes = await _uow.Repository<PaieEcriture>().ListReadOnlyAsync(c => c.BddSage == sageDb, ct);
        var premiere = lignes.OrderBy(l => l.Id).FirstOrDefault();
        var selection = await GetSelectionAsync(sageDb, ct);
        var variables = PayrollVariableCatalog.Toutes.Where(d => d.Envoyable)
            .Select(d => new PayrollWriteTargetDto(d.Id, d.Libelle, selection.EstActive(d.Id),
                lignes.FirstOrDefault(l => Egal(l.Categorie, d.Id))?.ColValeur, DecimalesRequises(d)))
            .ToList();
        var tables = await SchemaOuVideAsync(s => s.ListTablesAsync(ct), sageDb);
        IReadOnlyList<PayrollTargetColumnDto> colonnes = Array.Empty<PayrollTargetColumnDto>();
        if (premiere is not null && TableDboOuNull(premiere.TableCible) is { } table)
            colonnes = (await SchemaOuVideAsync(s => s.ListColumnsAsync(table, ct), sageDb)).Select(ColonneDto).ToList();
        var bloquante = await PeriodeBloquanteAsync(sageDb, ct);
        return new PayrollWriteTargetsDto(sageDb, premiere?.TableCible, premiere?.ColMatricule, variables, tables, colonnes,
            bloquante is not null, bloquante);
    }

    /// <summary>Lecture du schéma pour l'affichage : en cas d'échec, liste vide (l'enregistrement, lui, échoue).</summary>
    private async Task<IReadOnlyList<T>> SchemaOuVideAsync<T>(Func<IPayrollTargetSchema, Task<IReadOnlyList<T>>> lire, string sageDb)
    {
        if (_schema is null)
            return Array.Empty<T>();
        try
        {
            return await lire(_schema);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Schéma de la base de paie {Base} illisible.", sageDb);
            return Array.Empty<T>();
        }
    }

    private IPayrollTargetSchema Schema() =>
        _schema ?? throw new DomainException("Le schéma de la base de paie ne peut pas être lu.");

    /// <summary>Nom de table du schéma dbo (seul schéma pris en charge, comme l'écriture de paie).</summary>
    private static string TableDbo(string table)
    {
        var (schema, nom) = PayrollWritingConfiguration.SplitTable(table);
        if (!Egal(schema, "dbo"))
            throw new DomainException("Seules les tables du schéma dbo sont prises en charge.");
        return nom;
    }

    private static string? TableDboOuNull(string table)
    {
        try
        {
            return TableDbo(table);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    private async Task<BddSage> AutoriserAutreAsync(CancellationToken ct)
    {
        var database = await AutoriserAsync(ct);
        if (!EstAutre(database))
            throw new DomainException("Les destinations des variables se configurent uniquement pour une base AUTRE.");
        return database;
    }

    /// <summary>Accès à la base active, puis droit de modifier cette base SAGE (comme l'écran Bases de données).</summary>
    private async Task<BddSage> AutoriserAsync(CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var database = await _tenant.GetSageRowAsync(ct);
        await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, database.NomBd, ct);
        if (string.IsNullOrWhiteSpace(database.NomBd))
            throw new DomainException("La base SAGE active n'a pas de nom.");
        return database;
    }

    private async Task RefuserSiMoisEnvoyeAsync(string sageDb, CancellationToken ct)
    {
        var bloquante = await PeriodeBloquanteAsync(sageDb, ct);
        if (bloquante is not null)
            throw new ConflictException(
                $"Mois déjà envoyé en paie (période {bloquante}) : modification impossible. "
                + "La configuration pourra être modifiée le mois prochain et s'appliquera alors à la période suivante.");
    }

    private async Task<string?> PeriodeBloquanteAsync(string sageDb, CancellationToken ct)
    {
        var aujourdHui = _horloge.GetLocalNow().Date;
        var periodes = await _sendLog.ListPeriodsAsync(sageDb, ct);
        var bloquante = periodes
            .Where(p => PayrollVariableRules.MoisEnCoursEnvoye([p], aujourdHui))
            .OrderByDescending(p => p.Fin)
            .Select(p => ((DateTime Debut, DateTime Fin)?)p)
            .FirstOrDefault();
        return bloquante is { } p2 ? $"{p2.Debut:yyyy-MM-dd} → {p2.Fin:yyyy-MM-dd}" : null;
    }

    private Task<List<PaieVariable>> LireAsync(string sageDb, CancellationToken ct) =>
        _uow.Repository<PaieVariable>().ListReadOnlyAsync(v => v.BddSage == sageDb, ct);

    /// <summary>
    /// Sans ligne : valeurs par défaut. Variable sans ligne : sa valeur par défaut. Les codes de paie propres à la base
    /// (variables hors catalogue commun : Retard, Absence) accompagnent la sélection.
    /// </summary>
    internal static PayrollVariableSelection Selection(IReadOnlyList<PaieVariable> lignes)
    {
        if (lignes.Count == 0)
            return PayrollVariableSelection.Default;
        var actives = PayrollVariableSelection.Toutes.Where(nom =>
        {
            var ligne = lignes.FirstOrDefault(l => string.Equals(l.Variable, nom, StringComparison.OrdinalIgnoreCase));
            return ligne?.Actif ?? PayrollVariableSelection.Default.EstActive(nom);
        });
        var codes = lignes
            .Where(l => !string.IsNullOrWhiteSpace(l.CodePaie) && PayrollVariableCatalog.Trouver(l.Variable) is { CodeCommun: false })
            .GroupBy(l => PayrollVariableCatalog.Definition(l.Variable).Id)
            .ToDictionary(g => g.Key, g => g.First().CodePaie!.Trim(), StringComparer.Ordinal);
        return PayrollVariableSelection.Avec(actives) with { CodesPaie = codes };
    }

    private async Task<PayrollVariablesConfigDto> ConstruireAsync(BddSage database, IReadOnlyList<PaieVariable> lignes, CancellationToken ct)
    {
        var sageDb = database.NomBd!;
        var autre = EstAutre(database);
        var selection = Selection(lignes);
        var cibles = autre ? await CiblesAutreAsync(sageDb, ct) : CiblesSage(await _codes.GetOvertimeSageCodesAsync(ct));
        var variables = PayrollVariableCatalog.Toutes.Select(definition =>
        {
            var nom = definition.Id;
            var ligne = lignes.FirstOrDefault(l => string.Equals(l.Variable, nom, StringComparison.OrdinalIgnoreCase));
            var propre = !definition.CodeCommun;
            // Cible en lecture seule : code commun (base SAGE) ou destination T_PAIE_ECRITURE (autre base, variable envoyable).
            var cible = (autre ? definition.Envoyable : definition.CodeCommun) ? cibles.GetValueOrDefault(nom) : null;
            return new PayrollVariableDto(nom, definition.Libelle, selection.EstActive(nom),
                propre ? ligne?.CodePaie : null, propre ? ligne?.Unite : null, !definition.Envoyable ? ligne?.ColCible : null,
                cible, !definition.Envoyable);
        }).ToList();
        var bloquante = await PeriodeBloquanteAsync(sageDb, ct);
        var colonnes = autre ? await ColonnesAutreAsync(database, ct) : Array.Empty<string>();
        var (codesPaie, avertissement) = autre
            ? (Array.Empty<SageConstantOptionDto>(), null)
            : await CodesSageAsync(sageDb, ct);
        return new PayrollVariablesConfigDto(sageDb, autre ? "AUTRE" : "STANDARD", lignes.Count == 0,
            bloquante is not null, bloquante, variables, colonnes)
        {
            CodesPaie = codesPaie,
            AvertissementCodes = avertissement
        };
    }

    /// <summary>Codes de paie lus dans SAGE (T_CST) pour la liste déroulante ; en cas d'échec, liste vide et message.</summary>
    private async Task<(IReadOnlyList<SageConstantOptionDto> Codes, string? Avertissement)> CodesSageAsync(string sageDb, CancellationToken ct)
    {
        try
        {
            return (await _codes.ListSageConstantOptionsAsync(ct) ?? Array.Empty<SageConstantOptionDto>(), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Codes de paie de la base {Base} illisibles.", sageDb);
            return (Array.Empty<SageConstantOptionDto>(), "Les codes de paie n'ont pas pu être lus dans SAGE.");
        }
    }

    private static Dictionary<string, string> CiblesSage(OvertimeSageCodes codes) =>
        PayrollVariableSelection.HeuresSup.ToDictionary(nom => nom, codes.CodePour);

    /// <summary>Destinations T_PAIE_ECRITURE des variables envoyables (heures supplémentaires et Retard).</summary>
    private async Task<Dictionary<string, string>> CiblesAutreAsync(string sageDb, CancellationToken ct) =>
        (await _uow.Repository<PaieEcriture>().ListReadOnlyAsync(c => c.BddSage == sageDb, ct))
            .Where(c => PayrollWritingConfiguration.CategoriesConnues.Contains(c.Categorie, StringComparer.OrdinalIgnoreCase))
            .GroupBy(c => PayrollWritingConfiguration.NormalizeCategory(c.Categorie))
            .ToDictionary(g => g.Key, g => g.First().ColValeur);

    /// <summary>Colonnes de la table d'écriture configurée (schéma dbo) ; liste vide si elle n'est pas lisible.</summary>
    private async Task<IReadOnlyList<string>> ColonnesAutreAsync(BddSage database, CancellationToken ct)
    {
        var config = (await _uow.Repository<PaieEcriture>().ListReadOnlyAsync(c => c.BddSage == database.NomBd, ct)).FirstOrDefault();
        if (config is null)
            return Array.Empty<string>();
        try
        {
            var (schema, table) = PayrollWritingConfiguration.SplitTable(config.TableCible);
            if (!string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<string>();
            return (await _discovery.ListColumnsAsync(SqlServerCredentials.Of(database), database.NomBd!, table, ct))
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Colonnes de la table d'écriture de la base {Base} illisibles.", database.NomBd);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Autre base : colonne saisie d'une variable non envoyable (Absence, lot B) au format identifiant SQL et, si la liste
    /// est connue, existante. La colonne d'une variable envoyable vient de T_PAIE_ECRITURE, vérifiée à son enregistrement.
    /// </summary>
    private async Task<IReadOnlyList<string>> ValiderColonnesAsync(BddSage database, IReadOnlyList<PayrollVariableSetting> lignes, CancellationToken ct)
    {
        var erreurs = new List<string>();
        var aVerifier = lignes.Where(l => !string.IsNullOrWhiteSpace(l.ColonneCible)
                                          && PayrollVariableSelection.SansEnvoi.Contains(l.Variable, StringComparer.OrdinalIgnoreCase)).ToList();
        if (aVerifier.Count == 0)
            return erreurs;
        var colonnes = await ColonnesAutreAsync(database, ct);
        foreach (var ligne in aVerifier)
        {
            var libelle = PayrollVariableRules.Libelle(PayrollVariableSelection.Normaliser(ligne.Variable));
            try
            {
                PayrollWritingConfiguration.ValidateIdentifier(ligne.ColonneCible, $"La colonne cible de la variable {libelle}");
            }
            catch (DomainException exception)
            {
                erreurs.Add(exception.Message);
                continue;
            }
            if (colonnes.Count > 0 && !colonnes.Contains(ligne.ColonneCible!, StringComparer.OrdinalIgnoreCase))
                erreurs.Add($"La colonne {ligne.ColonneCible} de la variable {libelle} n'existe pas dans la table d'écriture.");
        }
        return erreurs;
    }

    private static bool EstAutre(BddSage database) => string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase);

    private static bool Egal(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Texte(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();
}
