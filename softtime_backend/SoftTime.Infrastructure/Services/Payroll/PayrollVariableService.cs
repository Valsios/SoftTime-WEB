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
/// Variables envoyées à la paie, par base SAGE (T_PAIE_VARIABLE). Le calcul n'en dépend jamais : seuls les writers de
/// paie et le Contrôle avant paie lisent la sélection. Les modifications passent par EF, donc par le journal central
/// (qui, quand, avant, après).
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
        ILogger<PayrollVariableService> logger)
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
        return new PayrollVariablesActiveDto(_tenant.SageDb,
            PayrollVariableSelection.Toutes.Where(selection.EstActive).ToList());
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
        var lignes = (variables ?? Array.Empty<PayrollVariableSaveDto>())
            .Select(v => new PayrollVariableSetting(v.Variable, v.Actif, Texte(v.CodePaie), Texte(v.Unite)?.ToUpperInvariant(), Texte(v.ColonneCible)))
            .ToList();
        var erreurs = PayrollVariableRules.Valider(lignes, baseSage).ToList();
        if (!baseSage)
            erreurs.AddRange(await ValiderColonnesAsync(database, lignes, ct));
        if (erreurs.Count > 0)
            throw new DomainException(string.Join(" ", erreurs));

        await RefuserSiMoisEnvoyeAsync(sageDb, ct);

        var repo = _uow.Repository<PaieVariable>();
        var existantes = await repo.ListAsync(v => v.BddSage == sageDb, ct);
        var maintenant = _horloge.GetUtcNow().UtcDateTime;
        foreach (var ligne in lignes)
        {
            var nom = PayrollVariableSelection.Normaliser(ligne.Variable);
            var sansEnvoi = PayrollVariableSelection.SansEnvoi.Contains(nom);
            var code = sansEnvoi && baseSage ? ligne.CodePaie : null;
            var colonne = sansEnvoi && !baseSage ? ligne.ColonneCible : null;
            var unite = sansEnvoi ? ligne.Unite : null;
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

    /// <summary>Sans ligne : valeurs par défaut. Variable sans ligne : sa valeur par défaut.</summary>
    internal static PayrollVariableSelection Selection(IReadOnlyList<PaieVariable> lignes)
    {
        if (lignes.Count == 0)
            return PayrollVariableSelection.Default;
        var actives = PayrollVariableSelection.Toutes.Where(nom =>
        {
            var ligne = lignes.FirstOrDefault(l => string.Equals(l.Variable, nom, StringComparison.OrdinalIgnoreCase));
            return ligne?.Actif ?? PayrollVariableSelection.Default.EstActive(nom);
        });
        return PayrollVariableSelection.Avec(actives);
    }

    private async Task<PayrollVariablesConfigDto> ConstruireAsync(BddSage database, IReadOnlyList<PaieVariable> lignes, CancellationToken ct)
    {
        var sageDb = database.NomBd!;
        var autre = EstAutre(database);
        var selection = Selection(lignes);
        var cibles = autre ? await CiblesAutreAsync(sageDb, ct) : CiblesSage(await _codes.GetOvertimeSageCodesAsync(ct));
        var variables = PayrollVariableSelection.Toutes.Select(nom =>
        {
            var ligne = lignes.FirstOrDefault(l => string.Equals(l.Variable, nom, StringComparison.OrdinalIgnoreCase));
            var sansEnvoi = PayrollVariableSelection.SansEnvoi.Contains(nom);
            return new PayrollVariableDto(nom, PayrollVariableRules.Libelle(nom), selection.EstActive(nom),
                sansEnvoi ? ligne?.CodePaie : null, sansEnvoi ? ligne?.Unite : null, sansEnvoi ? ligne?.ColCible : null,
                sansEnvoi ? null : cibles.GetValueOrDefault(nom), sansEnvoi);
        }).ToList();
        var bloquante = await PeriodeBloquanteAsync(sageDb, ct);
        var colonnes = autre ? await ColonnesAutreAsync(database, ct) : Array.Empty<string>();
        return new PayrollVariablesConfigDto(sageDb, autre ? "AUTRE" : "STANDARD", lignes.Count == 0,
            bloquante is not null, bloquante, variables, colonnes);
    }

    private static Dictionary<string, string> CiblesSage(OvertimeSageCodes codes) =>
        PayrollVariableSelection.HeuresSup.ToDictionary(nom => nom, codes.CodePour);

    private async Task<Dictionary<string, string>> CiblesAutreAsync(string sageDb, CancellationToken ct) =>
        (await _uow.Repository<PaieEcriture>().ListReadOnlyAsync(c => c.BddSage == sageDb, ct))
            .Where(c => PayrollVariableSelection.HeuresSup.Contains(c.Categorie, StringComparer.OrdinalIgnoreCase))
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

    /// <summary>Autre base : colonnes de Retard et Absence au format identifiant SQL et, si la liste est connue, existantes.</summary>
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

    private static string? Texte(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();
}
