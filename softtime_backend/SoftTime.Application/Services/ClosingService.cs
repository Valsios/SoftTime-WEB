using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Analyse de clôture de paie, en lecture seule : charge en une passe (ensembliste) le planning,
/// les pointages, les corrections, les semaines validées, les fériés et les congés SAGE d'une
/// période et de la période précédente de même longueur, puis applique toutes les règles de
/// <see cref="PayrollClosingRules"/>. Aucune écriture en base, aucun appel au journal.
///
/// Modèle d'orchestration : <see cref="DashboardService"/> (base active et droits via
/// <see cref="TenantConnectionService.EnsureAuthorizedAsync"/>, congés via
/// <see cref="SageLeaveCalendar"/>, filtre département appliqué aux cartes avant tout calcul).
///
/// Conventions :
/// <list type="bullet">
///   <item>le jour en cours et les jours futurs ne sont jamais contrôlés : la période est plafonnée
///     à la veille (une journée entamée n'est pas encore complète) ;</item>
///   <item>un jour-employé contrôlé est un couple (salarié, jour) de la période dont le planning
///     retenu est un jour travaillé avec heures, ou qui porte au moins un pointage ;</item>
///   <item>seuls des matricules (clé canonique) sont manipulés : aucun nom ni prénom.</item>
/// </list>
/// </summary>
public class ClosingService
{
    /// <summary>Longueur maximale par défaut de la période analysée, bornes comprises (réglage Closing:MaxPeriodDays).</summary>
    public const int MaxPeriodDays = ClosingOptions.PlafondPeriodDays;

    /// <summary>Taille de page par défaut et maximale de la liste d'anomalies (une demande supérieure est ramenée au maximum).</summary>
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    private static readonly IReadOnlyList<ClosingAnomalyType> Types = Enum.GetValues<ClosingAnomalyType>();
    private static readonly IReadOnlyList<ClosingSeverity> Gravites = Enum.GetValues<ClosingSeverity>();

    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly SageLeaveCalendar _leaves;
    private readonly TimeProvider _horloge;
    private readonly ClosingOptions _reglages;

    public ClosingService(
        IUnitOfWork uow,
        TenantConnectionService tenant,
        SageLeaveCalendar leaves,
        TimeProvider? horloge = null,
        ClosingOptions? reglages = null)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
        _horloge = horloge ?? TimeProvider.System;
        _reglages = reglages ?? new ClosingOptions();
    }

    /// <summary>
    /// Analyse d'une période. Sans options de règles dans la demande, les réglages « Closing »
    /// s'appliquent (tolérance, semaines validées, gravités, poids, seuil de statut).
    /// </summary>
    public async Task<ClosingAnalysisDto> AnalyserAsync(ClosingAnalysisRequest request, CancellationToken ct = default)
    {
        request = AvecPeriodeParDefaut(request, Today());
        request = request with { Options = request.Options ?? _reglages.ReglesParDefaut() };
        Valider(request, _reglages.MaxPeriodDays);
        var donnees = await ChargerAsync(request, ct);
        return Calculer(donnees, request);
    }

    /// <summary>
    /// Période par défaut, quand ni début ni fin ne sont fournis : le mois civil complet PRÉCÉDENT
    /// (le mois que l'on clôture ; le mois en cours n'est pas terminé). Une seule borne fournie reste
    /// une erreur de validation.
    /// </summary>
    internal static ClosingAnalysisRequest AvecPeriodeParDefaut(ClosingAnalysisRequest request, DateTime aujourdhui)
    {
        if (request.From != default || request.To != default)
            return request;
        var debutMois = new DateTime(aujourdhui.Year, aujourdhui.Month, 1).AddMonths(-1);
        return request with { From = debutMois, To = debutMois.AddMonths(1).AddDays(-1) };
    }

    /// <summary>
    /// Contrôle de la période et de la pagination, avec le même type d'exception que les autres
    /// services (traduit en HTTP 400) : page et taille de page doivent être au moins 1.
    /// </summary>
    internal static void Valider(ClosingAnalysisRequest request, int maxPeriodDays = MaxPeriodDays)
    {
        if (request.From == default || request.To == default)
            throw new InvalidOperationException("La période (from/to) est requise pour analyser la clôture.");
        if (request.To.Date < request.From.Date)
            throw new InvalidOperationException("La date de fin doit être postérieure ou égale à la date de début.");
        if (LongueurJours(request.From, request.To) > maxPeriodDays)
            throw new InvalidOperationException($"La période analysée ne peut pas dépasser {maxPeriodDays} jours.");
        if (request.Page is < 1)
            throw new InvalidOperationException("Le numéro de page doit être supérieur ou égal à 1.");
        if (request.PageSize is < 1)
            throw new InvalidOperationException("La taille de page doit être supérieure ou égale à 1.");
    }

    /// <summary>
    /// Chargement ensembliste de la période demandée (et de la période précédente seulement si la
    /// comparaison est demandée). Le filtre département s'applique aux cartes, puis toutes les autres
    /// données sont restreintes aux salariés retenus avant le calcul. Planning, pointages, corrections
    /// et semaines validées sont projetés sur les seules colonnes utiles aux règles : ces objets ne
    /// sont pas suivis par le contexte (lecture seule, mémoire réduite).
    /// </summary>
    internal async Task<ClosingData> ChargerAsync(ClosingAnalysisRequest request, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;

        var (debutPrecedent, _) = PeriodePrecedente(request.From, request.To);
        var debut = request.IncludeComparison ? debutPrecedent : request.From.Date;
        var fin = request.To.Date;

        var clock = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException("Paramètre pointeuse (T_CLOCK) manquant.");

        var cartes = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct))
            .Where(c => CorrespondPointeuse(c.POINTEUSE_BDD, pointeuseDb))
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_MATRICULE))
            .Where(c => string.IsNullOrWhiteSpace(request.Departement)
                        || string.Equals(c.DEPARTEMENT?.Trim(), request.Departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var idsCartes = cartes.Select(c => c.ID).ToHashSet();
        var cles = cartes.Select(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE)).ToHashSet(StringComparer.Ordinal);

        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();

        if (cartes.Count == 0)
            return new ClosingData(clock.MULTIPOINT == true, cartes,
                new List<T_PLANNING_SAL>(), new List<T_POINTAGE>(), new List<T_HEURECORRIGER>(),
                new List<T_HS_VALIDER>(), feries, new Dictionary<string, List<LeaveDay>>(), Today());

        // Marge d'un jour de planning de chaque côté (postes de nuit), comme le pré-traitement.
        var planDebut = debut.AddDays(-1);
        var planFin = fin.AddDays(1);
        var repoPlanning = _uow.Repository<T_PLANNING_SAL>();
        var plannings = (await repoPlanning.ToListAsync(repoPlanning.Query()
                .Where(p => p.BDD_SAGE == sageDb && p.DateP >= planDebut && p.DateP <= planFin)
                .OrderBy(p => p.ID)
                .Select(p => new T_PLANNING_SAL
                {
                    ID = p.ID, IDSAL = p.IDSAL, DateP = p.DateP, HA = p.HA, HD = p.HD, Pause = p.Pause, OffP = p.OffP
                }), ct))
            .Where(p => p.DateP.HasValue && idsCartes.Contains(p.IDSAL))
            .ToList();

        // Sorties de nuit : les pointages du lendemain de la fin de période sont aussi chargés.
        var pointagesFinExclusive = fin.AddDays(2);
        var repoPointage = _uow.Repository<T_POINTAGE>();
        var pointages = (await repoPointage.ToListAsync(repoPointage.Query()
                .Where(p => p.NOM_BDD_SAGE == sageDb && p.DATE_POINTAGE >= debut && p.DATE_POINTAGE < pointagesFinExclusive)
                .OrderBy(p => p.IDPOINTAGE)
                .Select(p => new T_POINTAGE
                {
                    IDPOINTAGE = p.IDPOINTAGE, MATRICULE_SAGE = p.MATRICULE_SAGE, DATE_POINTAGE = p.DATE_POINTAGE,
                    HEURE_POINTAGE = p.HEURE_POINTAGE, NOM_BDD_POINTEUSE = p.NOM_BDD_POINTEUSE
                }), ct))
            .Where(p => CorrespondPointeuse(p.NOM_BDD_POINTEUSE, pointeuseDb))
            .Where(p => cles.Contains(MatriculeIdentity.Cle(p.MATRICULE_SAGE)))
            .ToList();

        var correctionsFinExclusive = fin.AddDays(1);
        var repoCorrection = _uow.Repository<T_HEURECORRIGER>();
        var corrections = (await repoCorrection.ToListAsync(repoCorrection.Query()
                .Where(h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= debut && h.DATE_POINTAGE_IN < correctionsFinExclusive)
                .OrderBy(h => h.ID)
                .Select(h => new T_HEURECORRIGER
                {
                    ID = h.ID, MATRICULE_SAGE = h.MATRICULE_SAGE, DATE_POINTAGE_IN = h.DATE_POINTAGE_IN,
                    HENTREE = h.HENTREE, HSORTIE = h.HSORTIE, HPS = h.HPS, HPE = h.HPE,
                    ABSENCE_AM = h.ABSENCE_AM, ABSENCE_PM = h.ABSENCE_PM, INTITULE_ABSENCE = h.INTITULE_ABSENCE,
                    FERIER = h.FERIER, FERIER_AM = h.FERIER_AM, FERIER_PM = h.FERIER_PM, M_FERIES = h.M_FERIES,
                    ORIGINE = h.ORIGINE
                }), ct))
            .Where(h => cles.Contains(MatriculeIdentity.Cle(h.MATRICULE_SAGE)))
            .ToList();

        var lundiDebut = debut.AddDays(-(((int)debut.DayOfWeek + 6) % 7));
        var repoSemaine = _uow.Repository<T_HS_VALIDER>();
        var semainesValidees = (await repoSemaine.ToListAsync(repoSemaine.Query()
                .Where(v => v.BDD_SAGE == sageDb && v.VALIDATION_HS == true && v.DATE_DEB >= lundiDebut && v.DATE_DEB <= fin)
                .OrderBy(v => v.ID)
                .Select(v => new T_HS_VALIDER { ID = v.ID, MATRICULES = v.MATRICULES, DATE_DEB = v.DATE_DEB, VALIDATION_HS = v.VALIDATION_HS }), ct))
            .Where(v => cles.Contains(MatriculeIdentity.Cle(v.MATRICULES)))
            .ToList();

        var congesTenant = await _leaves.LoadAsync(ct);
        var conges = congesTenant
            .Where(kv => cles.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        return new ClosingData(clock.MULTIPOINT == true, cartes, plannings, pointages, corrections,
            semainesValidees, feries, conges, Today());
    }

    /// <summary>Calcul pur à partir des données chargées : aucune lecture ni écriture.</summary>
    internal static ClosingAnalysisDto Calculer(ClosingData donnees, ClosingAnalysisRequest request)
    {
        var options = request.Options ?? ClosingRulesOptions.Default;
        var from = request.From.Date;
        var to = request.To.Date;
        var (debutPrecedent, finPrecedente) = PeriodePrecedente(from, to);

        var departementParCle = donnees.Cartes
            .GroupBy(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => Departement(g.OrderBy(c => c.ID).First().DEPARTEMENT),
                StringComparer.Ordinal);

        var actuel = Controler(donnees, from, to, options);
        // Période précédente : calculée seulement si la comparaison est demandée (elle n'est alors chargée qu'à ce moment).
        var precedent = request.IncludeComparison ? Controler(donnees, debutPrecedent, finPrecedente, options) : null;

        var score = PayrollClosingRules.Score(actuel.Anomalies, actuel.JoursControles.Count, options);
        var parType = CompterTypes(actuel.Anomalies);

        var page = Math.Max(1, request.Page ?? 1);
        var taille = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        // Les comptages incluent toujours les « Information » ; la liste les omet sauf demande.
        var affichees = request.IncludeInformation
            ? actuel.Anomalies
            : actuel.Anomalies.Where(a => a.Severity != ClosingSeverity.Information).ToList();
        var total = affichees.Count;
        var items = affichees
            .Skip(PaginationRules.Decalage(page, taille))
            .Take(taille)
            .Select(a => new ClosingAnomalyDto(
                a.Type.ToString(),
                a.Severity.ToString(),
                options.Poids(a.Severity),
                a.Matricule,
                departementParCle.GetValueOrDefault(a.Matricule),
                a.Date,
                a.Detail,
                a.Heures,
                a.PointageExiste))
            .ToList();
        var anomaliesPage = new PagedResult<ClosingAnomalyDto>(
            items, total, PaginationRules.PageEffective(total, page), taille, PaginationRules.NombreDePages(total, taille));

        ClosingComparisonDto? comparaison = null;
        if (precedent is not null && precedent.JoursControles.Count > 0)
        {
            var scorePrecedent = PayrollClosingRules.Score(precedent.Anomalies, precedent.JoursControles.Count, options);
            var parTypePrecedent = CompterTypes(precedent.Anomalies);
            comparaison = new ClosingComparisonDto(
                debutPrecedent,
                finPrecedente,
                scorePrecedent,
                precedent.JoursControles.Count,
                score is int s && scorePrecedent is int sp ? s - sp : null,
                Types.Select(t => new ClosingTypeDeltaDto(
                        t.ToString(), parTypePrecedent[t], parType[t], parType[t] - parTypePrecedent[t]))
                    .ToList());
        }

        var departements = departementParCle.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d is null ? 1 : 0)
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parDepartement = departements
            .Select(dep =>
            {
                bool DuDepartement(string cle) =>
                    string.Equals(departementParCle.GetValueOrDefault(cle), dep, StringComparison.OrdinalIgnoreCase);
                var anomalies = actuel.Anomalies.Where(a => DuDepartement(a.Matricule)).ToList();
                var jours = actuel.JoursControles.Count(j => DuDepartement(j.Cle));
                return new ClosingDepartmentDto(
                    dep,
                    PayrollClosingRules.Score(anomalies, jours, options),
                    jours,
                    anomalies.Count,
                    ComptagesGravite(anomalies),
                    ComptagesType(anomalies));
            })
            .ToList();

        return new ClosingAnalysisDto(
            from,
            to,
            string.IsNullOrWhiteSpace(request.Departement) ? null : request.Departement.Trim(),
            score,
            PayrollClosingRules.FormuleScore,
            actuel.JoursControles.Count,
            actuel.Anomalies.Count,
            ComptagesGravite(actuel.Anomalies),
            ComptagesType(actuel.Anomalies),
            anomaliesPage,
            parDepartement,
            comparaison,
            ClosingStatusRules.Statut(actuel.Anomalies, actuel.JoursControles.Count, options.CriticalRatioBlocking)?.ToString(),
            actuel.SalariesSansPlanningExclus);
    }

    /// <summary>Toutes les règles de clôture sur une période plafonnée à la veille.</summary>
    internal static ResultatPeriode Controler(ClosingData d, DateTime from, DateTime to, ClosingRulesOptions options)
    {
        var hier = d.DateDuJour.Date.AddDays(-1);
        var fin = to < hier ? to : hier;
        if (from > fin)
            return new ResultatPeriode(Array.Empty<ClosingAnomaly>(), new HashSet<(string, DateTime)>(), 0);

        // Index des pointages construit une fois au chargement ; lignes d'absence calculées une fois
        // et partagées entre pointages manquants et plannings sans pointage.
        var index = d.IndexPointages;
        var lignesAbsence = AbsenceReportRules.Calculer(d.Plannings, d.Cartes, d.Corrections, d.Conges, d.Feries, from, fin, d.DateDuJour);

        var anomalies = new List<ClosingAnomaly>();
        anomalies.AddRange(PayrollClosingRules.PointagesIncomplets(
            d.Cartes, d.Plannings, index, d.Corrections, d.SemainesValidees, from, fin, d.MultiPointage, options));
        anomalies.AddRange(PayrollClosingRules.PointagesManquants(lignesAbsence, index));
        anomalies.AddRange(PayrollClosingRules.PlanningsSansPointage(lignesAbsence, index, d.Corrections));
        anomalies.AddRange(PayrollClosingRules.PointagesSansPlanning(d.Cartes, d.Plannings, index, from, fin, options));
        anomalies.AddRange(PayrollClosingRules.DoublonsExacts(index, from, fin, options, d.MultiPointage));
        anomalies.AddRange(PayrollClosingRules.QuasiDoublons(index, from, fin, options, d.MultiPointage));
        anomalies.AddRange(PayrollClosingRules.FeriesNonAppliques(
            d.Cartes, d.Plannings, d.Corrections, d.Feries, from, fin, d.DateDuJour, index));

        var exclus = options.ExclureSansPlanningSurPeriode
            ? PayrollClosingRules.SalariesSansPlanningSurPeriode(d.Cartes, d.Plannings, index, from, fin).Count
            : 0;
        return new ResultatPeriode(
            PayrollClosingRules.AppliquerGravites(PayrollClosingRules.Trier(anomalies), options),
            JoursControles(d, from, fin),
            exclus);
    }

    /// <summary>
    /// Jours-employé contrôlés : planning retenu travaillé avec heures (ligne de travail avant OFF,
    /// puis plus petit ID, comme le tableau de bord), ou au moins un pointage ce jour-là.
    /// </summary>
    private static HashSet<(string Cle, DateTime Jour)> JoursControles(ClosingData d, DateTime from, DateTime fin)
    {
        var cleParIdSal = new Dictionary<int, string>();
        foreach (var carte in d.Cartes)
            cleParIdSal[carte.ID] = MatriculeIdentity.Cle(carte.SAGE_MATRICULE);

        var jours = d.Plannings
            .Where(p => p.DateP!.Value.Date >= from && p.DateP!.Value.Date <= fin && cleParIdSal.ContainsKey(p.IDSAL))
            .GroupBy(p => (Cle: cleParIdSal[p.IDSAL], Jour: p.DateP!.Value.Date))
            .Select(g => (g.Key, Retenu: g.OrderBy(p => AbsenceReportRules.EstJourOff(p) ? 1 : 0).ThenBy(p => p.ID).First()))
            .Where(x => !AbsenceReportRules.EstJourOff(x.Retenu) && AbsenceReportRules.CalculerDureeHeures(x.Retenu) is not null)
            .Select(x => x.Key)
            .ToHashSet();

        foreach (var (cle, jour, _) in d.IndexPointages.Jours)
        {
            if (jour >= from && jour <= fin)
                jours.Add((cle, jour));
        }

        return jours;
    }

    private static Dictionary<ClosingAnomalyType, int> CompterTypes(IEnumerable<ClosingAnomaly> anomalies)
    {
        var comptes = Types.ToDictionary(t => t, _ => 0);
        foreach (var a in anomalies)
            comptes[a.Type]++;
        return comptes;
    }

    private static IReadOnlyList<ClosingCountDto> ComptagesType(IReadOnlyCollection<ClosingAnomaly> anomalies)
    {
        var comptes = CompterTypes(anomalies);
        return Types.Select(t => new ClosingCountDto(t.ToString(), comptes[t])).ToList();
    }

    private static IReadOnlyList<ClosingCountDto> ComptagesGravite(IReadOnlyCollection<ClosingAnomaly> anomalies) =>
        Gravites.Select(g => new ClosingCountDto(g.ToString(), anomalies.Count(a => a.Severity == g))).ToList();

    private static (DateTime Debut, DateTime Fin) PeriodePrecedente(DateTime from, DateTime to)
    {
        var longueur = LongueurJours(from, to);
        var fin = from.Date.AddDays(-1);
        return (fin.AddDays(-(longueur - 1)), fin);
    }

    private static int LongueurJours(DateTime from, DateTime to) => (to.Date - from.Date).Days + 1;

    private static string? Departement(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();

    private DateTime Today() => _horloge.GetLocalNow().Date;

    /// <summary>Filtre tenant pointeuse, en tolérant les lignes héritées sans base renseignée.</summary>
    private static bool CorrespondPointeuse(string? valeur, string pointeuseDb) =>
        string.IsNullOrWhiteSpace(valeur)
        || string.Equals(valeur.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);

    internal sealed record ResultatPeriode(
        IReadOnlyList<ClosingAnomaly> Anomalies,
        HashSet<(string Cle, DateTime Jour)> JoursControles,
        int SalariesSansPlanningExclus);
}

/// <summary>Données chargées pour une analyse de clôture (période demandée et période précédente).</summary>
internal sealed record ClosingData(
    bool MultiPointage,
    IReadOnlyList<T_CARDPAIE> Cartes,
    IReadOnlyList<T_PLANNING_SAL> Plannings,
    IReadOnlyList<T_POINTAGE> Pointages,
    IReadOnlyList<T_HEURECORRIGER> Corrections,
    IReadOnlyList<T_HS_VALIDER> SemainesValidees,
    IReadOnlySet<DateTime> Feries,
    IReadOnlyDictionary<string, List<LeaveDay>> Conges,
    DateTime DateDuJour)
{
    /// <summary>Pointages indexés une seule fois par (matricule, jour), partagés par toutes les règles.</summary>
    public ClosingPunchIndex IndexPointages { get; } = ClosingPunchIndex.Build(Pointages);
}
