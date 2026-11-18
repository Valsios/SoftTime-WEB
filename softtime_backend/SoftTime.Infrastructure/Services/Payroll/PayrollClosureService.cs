using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Closing;

namespace SoftTime.Infrastructure.Services.Payroll;

/// <summary>
/// Clôture de paie (T_PAIE_CLOTURE). Une seule action, définitive : AUCUNE méthode d'annulation, de déblocage ni de
/// suppression (et la base refuse toute modification, migration 010). Les contrôles sont faits avant d'écrire, puis
/// revérifiés sous le verrou applicatif partagé avec l'écriture des instantanés d'envoi.
/// </summary>
public sealed class PayrollClosureService : IPayrollClosureService
{
    public const string Envoye = "ENVOYEE";
    public const string Cloturee = "CLOTUREE";

    internal const string PeriodeNonTerminee = "La période n'est pas terminée : la clôture n'est possible qu'à partir du lendemain de sa date de fin.";
    internal const string AucunEnvoi = "Aucun envoi en paie n'existe pour cette période exacte : envoyez la période sans filtre, puis clôturez.";
    internal const string EnvoiFiltre = "Le dernier envoi de la période porte un filtre (département ou matricules). Lancez un envoi sans filtre avant de clôturer.";
    internal const string EnvoiIncertain = "Le dernier envoi de la période est INCERTAIN (son issue ou son instantané n'est pas garanti). Relancez l'envoi sans filtre avant de clôturer.";
    internal const string EnvoiNonReussi = "Le dernier envoi de la période n'a pas réussi (statut {0}). Relancez l'envoi sans filtre avant de clôturer.";
    internal const string SansInstantane = "Le dernier envoi de la période n'a pas d'instantané (envoi antérieur à la migration 009). Relancez l'envoi sans filtre avant de clôturer.";
    internal const string InstantaneAltere = "L'instantané du dernier envoi est altéré : l'empreinte recalculée diffère de l'empreinte enregistrée. Clôture refusée ; relancez l'envoi sans filtre.";
    internal const string ControleDesactive = "Le Contrôle avant paie est désactivé : la clôture, qui exige son statut « Prêt », est impossible.";
    internal const string DejaCloturee = "Période déjà clôturée ({0}) : une clôture est définitive.";
    internal const string Recouvrement = "La période recoupe une période déjà clôturée ({0}) : clôture refusée.";
    internal const string NonConcordant =
        "Les heures actuelles de la période ne correspondent plus à l'envoi figé ({0} valeur(s) différente(s), par exemple {1}) : "
        + "elles ont été recalculées depuis l'envoi. Relancez l'envoi sans filtre avant de clôturer.";

    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ICurrentUser _user;
    private readonly IEffectiveRightsService _rights;
    private readonly IClosingService _closing;
    private readonly ClosingOptions _closingOptions;
    private readonly AuditJournalOptions _journal;
    private readonly TimeProvider _horloge;
    private readonly PayrollVariableCache _variablesCache;
    private readonly ClosingAnalysisCache _analysisCache;
    private readonly IClosingChecklistCache _checklistCache;
    private readonly IPayrollClosureGuard _guard;
    private readonly ICompanyContext _company;
    private readonly ILogger<PayrollClosureService> _logger;

    public PayrollClosureService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ICurrentUser user,
        IEffectiveRightsService rights,
        IClosingService closing,
        IOptions<ClosingOptions> closingOptions,
        IOptions<AuditJournalOptions> journal,
        TimeProvider horloge,
        PayrollVariableCache variablesCache,
        ClosingAnalysisCache analysisCache,
        IClosingChecklistCache checklistCache,
        IPayrollClosureGuard guard,
        ICompanyContext company,
        ILogger<PayrollClosureService> logger)
    {
        _uow = uow;
        _tenant = tenant;
        _user = user;
        _rights = rights;
        _closing = closing;
        _closingOptions = closingOptions.Value;
        _journal = journal.Value;
        _horloge = horloge;
        _variablesCache = variablesCache;
        _analysisCache = analysisCache;
        _checklistCache = checklistCache;
        _guard = guard;
        _company = company;
        _logger = logger;
    }

    /// <summary>Code de permission de clôture d'une base (hors droits par défaut : accordé explicitement).</summary>
    public static string Permission(string baseSage) => $"SAGE:{baseSage}:CLOTURE";

    public async Task<PayrollClosureDto> CloseAsync(PayrollClosureRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var baseSage = await BaseAsync(ct);
        await FinePermissionAccess.EnsureAsync(_user, _rights, new FinePermission(Rights.Databases, Permission(baseSage)),
            FinePermissionAccess.ActionDenied, ct);
        var (debut, fin) = (request.From.Date, request.To.Date);
        if (debut > fin)
            throw new DomainException("La date de début doit précéder la date de fin.");
        if (!OvertimeRules.IsMaxOneMonth(debut, fin))
            throw new DomainException("La période d'une clôture ne peut pas dépasser un mois (période d'envoi en paie).");
        if (fin >= _horloge.GetLocalNow().Date)
            throw new DomainException(PeriodeNonTerminee);

        await RefuserSiClotureeAsync(baseSage, debut, fin, ct);
        var (envoi, instantane) = await DernierEnvoiValideAsync(baseSage, debut, fin, ct);
        var lignes = await PayrollSnapshotService.LignesAsync(_uow, instantane.Id, ct);
        if (!PayrollSnapshotService.Verifier(instantane, lignes).Integre)
            throw new ConflictException(InstantaneAltere);
        await VerifierConcordanceAsync(baseSage, instantane, lignes, ct);
        var (statut, resume) = await ControleAsync(debut, fin, ct);

        var login = string.IsNullOrWhiteSpace(_user.Login) ? "SYSTEME" : _user.Login!;
        var cloture = await _uow.RunExclusiveAsync(PayrollSendLog.SnapshotLock, async token =>
        {
            // Revérification sous verrou : aucune clôture concurrente, aucun envoi plus récent depuis les contrôles.
            await RefuserSiClotureeAsync(baseSage, debut, fin, token);
            var (dernier, _) = await DernierEnvoiValideAsync(baseSage, debut, fin, token);
            if (dernier.Id != envoi.Id)
                throw new ConflictException("Un nouvel envoi de la période a eu lieu pendant la clôture : relancez la clôture.");
            var nouvelle = new PaieCloture
            {
                BddSage = baseSage,
                PeriodeDebut = debut,
                PeriodeFin = fin,
                ClotureLe = DateTime.UtcNow,
                CloturePar = login,
                EnvoiId = envoi.Id,
                InstantaneId = instantane.Id,
                Empreinte = instantane.Empreinte.Trim(),
                StatutControle = statut,
                ResumeControle = resume
            };
            await _uow.Repository<PaieCloture>().AddAsync(nouvelle, token);
            if (_journal.Enabled)
                await _uow.Repository<JournalAudit>().AddAsync(Journal(nouvelle), token);
            await _uow.SaveChangesAsync(token);
            return nouvelle;
        }, ct);

        // Les analyses, checklists et sélections en cache ne doivent pas survivre à la clôture.
        _variablesCache.Invalider(baseSage);
        _analysisCache.Vider();
        _checklistCache.Vider();
        (_guard as PayrollClosureGuard)?.Oublier();
        _logger.LogInformation("Clôture de paie {Base} {Debut:yyyy-MM-dd} → {Fin:yyyy-MM-dd} par {Auteur}, empreinte {Empreinte}.",
            baseSage, debut, fin, login, cloture.Empreinte);
        return Dto(cloture);
    }

    public async Task<PayrollPeriodsDto> ListPeriodsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var baseSage = await BaseAsync(ct);
        var peutCloturer = await PeutAsync(Permission(baseSage), ct);
        if (!peutCloturer && !await PeutAsync($"SAGE:{baseSage}:UPDATE", ct))
            throw new ForbiddenException(FinePermissionAccess.ActionDenied);

        var envois = await _uow.Repository<PaieEnvoi>().ListReadOnlyAsync(e => e.BddSage == baseSage, ct);
        var instantanes = await _uow.Repository<PaieInstantane>().ListReadOnlyAsync(i => i.BddSage == baseSage, ct);
        var clotures = await _uow.Repository<PaieCloture>().ListReadOnlyAsync(c => c.BddSage == baseSage, ct);
        var periodes = envois
            .GroupBy(e => (Debut: e.PeriodeDebut.Date, Fin: e.PeriodeFin.Date))
            .Select(g =>
            {
                var dernier = g.OrderByDescending(e => e.EnvoyeLe).ThenByDescending(e => e.Id).First();
                var cloture = clotures.FirstOrDefault(c => c.PeriodeDebut.Date == g.Key.Debut && c.PeriodeFin.Date == g.Key.Fin);
                return new PayrollPeriodStateDto(g.Key.Debut, g.Key.Fin, cloture is null ? Envoye : Cloturee,
                    PayrollSnapshotService.Resume(dernier, instantanes.FirstOrDefault(i => i.EnvoiId == dernier.Id)),
                    cloture is null ? null : Dto(cloture));
            })
            .OrderByDescending(p => p.PeriodeDebut).ThenByDescending(p => p.PeriodeFin)
            .ToList();
        return new PayrollPeriodsDto(baseSage, peutCloturer, periodes);
    }

    public async Task<PayrollClosureStateDto> StateAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var (debut, fin) = from.Date <= to.Date ? (from.Date, to.Date) : (to.Date, from.Date);
        var recoupees = (await _guard.ClosedPeriodsAsync(ct)).Where(p => p.Recoupe(debut, fin)).ToList();
        return new PayrollClosureStateDto(recoupees.Count > 0, recoupees, recoupees.Count > 0 ? recoupees[0].MessageModification : null);
    }

    private async Task<string> BaseAsync(CancellationToken ct)
    {
        var database = await _tenant.GetSageRowAsync(ct);
        if (string.IsNullOrWhiteSpace(database.NomBd))
            throw new DomainException("La base SAGE active n'a pas de nom.");
        return database.NomBd;
    }

    private async Task<bool> PeutAsync(string permission, CancellationToken ct)
    {
        try
        {
            await FinePermissionAccess.EnsureAsync(_user, _rights, new FinePermission(Rights.Databases, permission), FinePermissionAccess.ActionDenied, ct);
            return true;
        }
        catch (ForbiddenException)
        {
            return false;
        }
    }

    private async Task RefuserSiClotureeAsync(string baseSage, DateTime debut, DateTime fin, CancellationToken ct)
    {
        var clotures = await _uow.Repository<PaieCloture>().ListReadOnlyAsync(c => c.BddSage == baseSage, ct);
        var periodes = clotures.Select(c => new ClosedPeriod(c.PeriodeDebut.Date, c.PeriodeFin.Date)).ToList();
        if (periodes.FirstOrDefault(p => p.Debut == debut && p.Fin == fin) is { } meme)
            throw new ConflictException(string.Format(CultureInfo.InvariantCulture, DejaCloturee, meme.Libelle));
        if (periodes.FirstOrDefault(p => p.Recoupe(debut, fin)) is { } autre)
            throw new ConflictException(string.Format(CultureInfo.InvariantCulture, Recouvrement, autre.Libelle));
    }

    /// <summary>Dernier envoi de la période exacte : réussi, sans filtre, avec instantané (sinon refus explicite).</summary>
    private async Task<(PaieEnvoi Envoi, PaieInstantane Instantane)> DernierEnvoiValideAsync(
        string baseSage, DateTime debut, DateTime fin, CancellationToken ct)
    {
        var envois = await _uow.Repository<PaieEnvoi>().ListReadOnlyAsync(
            e => e.BddSage == baseSage && e.PeriodeDebut == debut && e.PeriodeFin == fin, ct);
        var dernier = envois.OrderByDescending(e => e.EnvoyeLe).ThenByDescending(e => e.Id).FirstOrDefault()
                      ?? throw new ConflictException(AucunEnvoi);
        if (string.Equals(dernier.Statut, "INCERTAIN", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException(EnvoiIncertain);
        if (!string.Equals(dernier.Statut, "SUCCES", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException(string.Format(CultureInfo.InvariantCulture, EnvoiNonReussi, dernier.Statut));
        var instantane = (await _uow.Repository<PaieInstantane>().ListReadOnlyAsync(i => i.EnvoiId == dernier.Id, ct)).FirstOrDefault()
                         ?? throw new ConflictException(SansInstantane);
        if (instantane.AvecFiltre)
            throw new ConflictException(EnvoiFiltre);
        return (dernier, instantane);
    }

    /// <summary>
    /// Concordance avec les heures actuelles (T_HSExoImp) : chaque valeur figée doit être celle que l'envoi produirait
    /// aujourd'hui (même unité, même heures par jour) ; un zéro de remise à 0 ne doit pas avoir pris de valeur. Détecte un
    /// « Calculer » relancé après l'envoi. Les mises à jour sans ligne ne sont pas figées et ne sont donc pas comparées.
    /// </summary>
    private async Task VerifierConcordanceAsync(string baseSage, PaieInstantane instantane, IReadOnlyList<Domain.Models.PayrollSnapshotLine> lignes, CancellationToken ct)
    {
        var (debut, fin) = (instantane.PeriodeDebut.Date, instantane.PeriodeFin.Date);
        var heures = (await _uow.Repository<HsExoImp>().ListReadOnlyAsync(
                h => h.BddSage == baseSage && h.PeriodeDebut == debut && h.PeriodeFin == fin, ct))
            .GroupBy(h => MatriculeIdentity.Cle(h.Matricule))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var ecarts = new List<string>();
        foreach (var ligne in lignes)
        {
            var actuelle = 0m;
            if (heures.TryGetValue(MatriculeIdentity.Cle(ligne.Matricule), out var h))
            {
                var dto = new HsExoDto(h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150, h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence);
                var valeur = Domain.Models.PayrollVariableCatalog.Definition(ligne.Variable).ValeurEnvoyee(OvertimeAmounts.From(dto).Valeurs);
                actuelle = ligne.Unite == Domain.Models.PayrollUnits.Jours
                    ? Domain.Models.PayrollUnits.EnJours(valeur, instantane.HeuresParJour)
                    : valeur;
            }
            if (Domain.Models.PayrollSnapshotFingerprint.Arrondir(actuelle) != ligne.Valeur)
                ecarts.Add(string.Create(CultureInfo.InvariantCulture, $"{ligne.Variable} {ligne.Valeur} figé, {actuelle} actuel"));
        }
        if (ecarts.Count > 0)
            throw new ConflictException(string.Format(CultureInfo.InvariantCulture, NonConcordant, ecarts.Count, ecarts[0]));
    }

    /// <summary>Contrôle avant paie sur la base entière (sans département), recalculé sans cache : « Prêt » exigé.</summary>
    private async Task<(string Statut, string Resume)> ControleAsync(DateTime debut, DateTime fin, CancellationToken ct)
    {
        if (!_closingOptions.Enabled)
            throw new ConflictException(ControleDesactive);
        var analyse = await _closing.AnalyserAsync(new ClosingAnalysisRequest(
            debut, fin, Departement: null, Page: 1, PageSize: 10, IncludeInformation: false, ReuseCached: false,
            Gravites: new[] { Domain.Models.ClosingSeverity.Critique }, TriParGravite: true, Perimetre: "cloture-paie"), ct);
        var critiques = analyse.ParGravite.Where(g => g.Cle == nameof(Domain.Models.ClosingSeverity.Critique)).Sum(g => g.Nombre);
        var resume = Resume(analyse, critiques);
        if (!string.Equals(analyse.Statut, nameof(ClosingStatus.Pret), StringComparison.Ordinal))
            throw new ConflictException(RefusControle(analyse, critiques));
        return (analyse.Statut!, resume);
    }

    internal static string Resume(ClosingAnalysisDto a, int critiques) => Tronquer(string.Create(CultureInfo.InvariantCulture,
        $"Statut={Libelle(a.Statut)}; Score={a.Score?.ToString(CultureInfo.InvariantCulture) ?? "-"}; JoursContrôlés={a.JoursEmployeControles}; "
        + $"Anomalies={a.NombreAnomalies}; Critiques={critiques}; SalariésSansPlanningExclus={a.SalariesSansPlanningExclus}"), 1000);

    /// <summary>Refus explicite : statut, comptages, types d'anomalie et premières anomalies critiques à corriger.</summary>
    internal static string RefusControle(ClosingAnalysisDto a, int critiques)
    {
        if (a.Statut is null)
            return "Contrôle avant paie sans statut sur la période (aucun jour contrôlé) : clôture refusée. Le statut « Prêt » est exigé.";
        var types = string.Join(", ", a.ParType.Where(t => t.Nombre > 0).OrderByDescending(t => t.Nombre).Select(t => $"{t.Cle} ({t.Nombre})"));
        var exemples = string.Join(" ; ", a.Anomalies.Items.Take(10)
            .Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Date:dd/MM/yyyy} {x.Matricule} {x.Type}")));
        return Tronquer($"Contrôle avant paie « {Libelle(a.Statut)} » sur la base entière : clôture refusée, le statut « Prêt » est exigé. "
                        + $"Points à corriger : {critiques} anomalie(s) critique(s) sur {a.NombreAnomalies} ; types : {types}."
                        + (exemples.Length > 0 ? $" Anomalies critiques : {exemples}." : string.Empty), 2000);
    }

    private static string Libelle(string? statut) =>
        Enum.TryParse<ClosingStatus>(statut, out var s) ? ClosingStatusRules.Libelle(s) : statut ?? "aucun";

    private static string Tronquer(string texte, int max) => texte.Length <= max ? texte : texte[..(max - 1)] + "…";

    private JournalAudit Journal(PaieCloture c) => new()
    {
        DateUtc = DateTime.UtcNow,
        Tables = "T_PAIE_CLOTURE",
        Actions = "CLOTURE_PAIE",
        Nouveaux = Tronquer(string.Create(CultureInfo.InvariantCulture,
            $"Période={c.PeriodeDebut:yyyy-MM-dd}..{c.PeriodeFin:yyyy-MM-dd}; Envoi={c.EnvoiId}; Empreinte={c.Empreinte}; "
            + $"Contrôle={c.ResumeControle}"), 4000),
        Users = c.CloturePar,
        BaseSage = c.BddSage,
        BasePointeuse = _company.PointeuseDatabase,
        Correlations = Guid.NewGuid(),
        Source = "CLOTURE_PAIE"
    };

    internal static PayrollClosureDto Dto(PaieCloture c) => new(
        c.Id, c.BddSage, c.PeriodeDebut, c.PeriodeFin, c.ClotureLe, c.CloturePar, c.EnvoiId, c.InstantaneId,
        c.Empreinte.Trim(), c.StatutControle, c.ResumeControle);
}
