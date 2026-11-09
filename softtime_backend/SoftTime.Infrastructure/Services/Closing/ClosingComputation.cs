using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Closing;

internal static class ClosingComputation
{
    /// <summary>Longueur maximale par défaut de la période analysée, bornes comprises (réglage Closing:MaxPeriodDays).</summary>
    internal const int MaxPeriodDays = ClosingOptions.PlafondPeriodDays;

    /// <summary>Taille de page par défaut et maximale de la liste d'anomalies (une demande supérieure est ramenée au maximum).</summary>
    internal const int DefaultPageSize = 50;

    internal const int MaxPageSize = 200;

    internal static readonly IReadOnlyList<ClosingAnomalyType> Types = Enum.GetValues<ClosingAnomalyType>();

    internal static readonly IReadOnlyList<ClosingSeverity> Gravites = Enum.GetValues<ClosingSeverity>();

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
            throw new DomainException("La période (from/to) est requise pour analyser la clôture.");
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de fin doit être postérieure ou égale à la date de début.");
        if (LongueurJours(request.From, request.To) > maxPeriodDays)
            throw new DomainException($"La période analysée ne peut pas dépasser {maxPeriodDays} jours.");
        if (request.Page is < 1)
            throw new DomainException("Le numéro de page doit être supérieur ou égal à 1.");
        if (request.PageSize is < 1)
            throw new DomainException("La taille de page doit être supérieure ou égale à 1.");
    }

    /// <summary>Calcul pur à partir des données chargées : aucune lecture ni écriture.</summary>
    internal static ClosingAnalysisDto Calculer(ClosingData donnees, ClosingAnalysisRequest request)
        => Assembler(Preparer(donnees, request), request);

    /// <summary>
    /// Toute l'analyse sauf la page de la liste : c'est la partie coûteuse, identique d'une page à l'autre.
    /// Elle est calculée une fois puis, sur demande (<see cref="ClosingAnalysisRequest.ReuseCached"/>), réutilisée
    /// pendant une courte durée pour les changements de page.
    /// </summary>
    internal static AnalysePreparee Preparer(ClosingData donnees, ClosingAnalysisRequest request)
    {
        var options = request.Options ?? ClosingRulesOptions.Default;
        var from = request.From.Date;
        var to = request.To.Date;
        (DateTime Debut, DateTime Fin) periodePrecedente = PeriodePrecedente(from, to);
        var departementParCle = donnees.Cartes
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => Departement(g.OrderBy(c => c.Id).First().Departement),
                StringComparer.Ordinal);

        var actuel = Controler(donnees, from, to, options);
        // Période précédente : calculée seulement si la comparaison est demandée (elle n'est alors chargée qu'à ce moment).
        var precedent = request.IncludeComparison
            ? Controler(donnees, periodePrecedente.Debut, periodePrecedente.Fin, options)
            : null;
        var score = PayrollClosingRules.Score(actuel.Anomalies, actuel.JoursControles.Count, options);
        var comparaison = precedent is not null && precedent.JoursControles.Count > 0
            ? Comparer(actuel, precedent, periodePrecedente, options)
            : null;

        return new AnalysePreparee(
            options, from, to, departementParCle, actuel, score, comparaison,
            ParDepartement(actuel, departementParCle, options));
    }

    private static ClosingComparisonDto Comparer(
        ResultatPeriode actuel, ResultatPeriode precedent, (DateTime Debut, DateTime Fin) periode, ClosingRulesOptions options)
    {
        var score = PayrollClosingRules.Score(actuel.Anomalies, actuel.JoursControles.Count, options);
        var scorePrecedent = PayrollClosingRules.Score(precedent.Anomalies, precedent.JoursControles.Count, options);
        var parType = CompterTypes(actuel.Anomalies);
        var parTypePrecedent = CompterTypes(precedent.Anomalies);
        return new ClosingComparisonDto(
            periode.Debut,
            periode.Fin,
            scorePrecedent,
            precedent.JoursControles.Count,
            score is int s && scorePrecedent is int sp ? s - sp : null,
            Types.Select(t => new ClosingTypeDeltaDto(
                    t.ToString(), parTypePrecedent[t], parType[t], parType[t] - parTypePrecedent[t]))
                .ToList());
    }

    /// <summary>Score et comptages par département (département non renseigné en dernier).</summary>
    private static List<ClosingDepartmentDto> ParDepartement(
        ResultatPeriode actuel, Dictionary<string, string?> departementParCle, ClosingRulesOptions options) =>
        departementParCle.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d is null ? 1 : 0)
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
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

    /// <summary>Découpe la page demandée dans l'analyse préparée et assemble le résultat (rapide).</summary>
    internal static ClosingAnalysisDto Assembler(AnalysePreparee prep, ClosingAnalysisRequest request)
    {
        var actuel = prep.Actuel;
        return new ClosingAnalysisDto(
            prep.From,
            prep.To,
            string.IsNullOrWhiteSpace(request.Departement) ? null : request.Departement.Trim(),
            prep.Score,
            PayrollClosingRules.FormuleScore,
            actuel.JoursControles.Count,
            actuel.Anomalies.Count,
            ComptagesGravite(actuel.Anomalies),
            ComptagesType(actuel.Anomalies),
            PageAnomalies(prep, request),
            prep.ParDepartement,
            prep.Comparaison,
            ClosingStatusRules.Statut(actuel.Anomalies, actuel.JoursControles.Count, prep.Options.CriticalRatioBlocking)?.ToString(),
            actuel.SalariesSansPlanningExclus);
    }

    /// <summary>Page d'anomalies demandée, filtrée et triée par <see cref="AnomaliesAffichees"/>.</summary>
    private static PagedResult<ClosingAnomalyDto> PageAnomalies(AnalysePreparee prep, ClosingAnalysisRequest request)
    {
        var page = Math.Max(1, request.Page ?? 1);
        var taille = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        var affichees = AnomaliesAffichees(prep.Actuel.Anomalies, request);
        var total = affichees.Count;
        var items = affichees
            .Skip(PaginationRules.Decalage(page, taille))
            .Take(taille)
            .Select(a => new ClosingAnomalyDto(
                a.Type.ToString(),
                a.Severity.ToString(),
                prep.Options.Poids(a.Severity),
                a.Matricule,
                prep.DepartementParCle.GetValueOrDefault(a.Matricule),
                a.Date,
                a.Detail,
                a.Heures,
                a.PointageExiste))
            .ToList();
        return new PagedResult<ClosingAnomalyDto>(
            items, total, PaginationRules.PageEffective(total, page), taille, PaginationRules.NombreDePages(total, taille));
    }

    /// <summary>
    /// Anomalies de la LISTE. Les « Information » sont comptées mais omises sauf demande ; un filtre de gravité
    /// explicite remplace cette règle. Le tri par gravité est stable : l'ordre de départ, déterministe, départage les
    /// égalités. Seule la liste change : comptages, score et statut sont toujours calculés sur l'analyse complète.
    /// </summary>
    internal static IReadOnlyList<ClosingAnomaly> AnomaliesAffichees(
        IReadOnlyList<ClosingAnomaly> anomalies, ClosingAnalysisRequest request)
    {
        IReadOnlyList<ClosingAnomaly> affichees = request.Gravites is { Count: > 0 } filtre
            ? anomalies.Where(a => filtre.Contains(a.Severity)).ToList()
            : request.IncludeInformation
                ? anomalies
                : anomalies.Where(a => a.Severity != ClosingSeverity.Information).ToList();
        return request.TriParGravite
            ? affichees.OrderByDescending(a => (int)a.Severity).ToList()
            : affichees;
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
        var sources = new EmployeeSources(d.Cartes, d.Plannings, d.Corrections);
        var periode = new ReportPeriod(from, fin, d.DateDuJour);
        var scope = new ClosingScope(from, fin, d.MultiPointage, options);
        var lignesAbsence = AbsenceReportRules.Calculer(sources, new AbsenceCalendar(d.Conges, d.Feries), periode);

        var anomalies = new List<ClosingAnomaly>();
        anomalies.AddRange(PunchCompletenessRules.PointagesIncomplets(sources, index, d.SemainesValidees, scope));
        anomalies.AddRange(PunchCompletenessRules.PointagesManquants(lignesAbsence, index));
        anomalies.AddRange(PlanningCoverageRules.PlanningsSansPointage(lignesAbsence, index, d.Corrections));
        anomalies.AddRange(PlanningCoverageRules.PointagesSansPlanning(d.Cartes, d.Plannings, index, scope));
        anomalies.AddRange(DuplicatePunchRules.DoublonsExacts(index, scope));
        anomalies.AddRange(DuplicatePunchRules.QuasiDoublons(index, scope));
        anomalies.AddRange(HolidayPunchRules.FeriesNonAppliques(sources, d.Feries, periode, index));

        var exclus = options.ExclureSansPlanningSurPeriode
            ? PlanningCoverageRules.SalariesSansPlanningSurPeriode(d.Cartes, d.Plannings, index, scope).Count
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
    internal static HashSet<(string Cle, DateTime Jour)> JoursControles(ClosingData d, DateTime from, DateTime fin)
    {
        var cleParIdSal = new Dictionary<int, string>();
        foreach (var carte in d.Cartes)
            cleParIdSal[carte.Id] = MatriculeIdentity.Cle(carte.SageMatricule);

        var jours = d.Plannings
            .Where(p => p.DateP!.Value.Date >= from && p.DateP!.Value.Date <= fin && cleParIdSal.ContainsKey(p.IdSal))
            .GroupBy(p => (Cle: cleParIdSal[p.IdSal], Jour: p.DateP!.Value.Date))
            .Select(g => (g.Key, Retenu: g.OrderBy(p => AbsenceReportRules.EstJourOff(p) ? 1 : 0).ThenBy(p => p.Id).First()))
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

    internal static Dictionary<ClosingAnomalyType, int> CompterTypes(IEnumerable<ClosingAnomaly> anomalies)
    {
        var comptes = Types.ToDictionary(t => t, _ => 0);
        foreach (var a in anomalies)
            comptes[a.Type]++;
        return comptes;
    }

    internal static IReadOnlyList<ClosingCountDto> ComptagesType(IReadOnlyCollection<ClosingAnomaly> anomalies)
    {
        var comptes = CompterTypes(anomalies);
        return Types.Select(t => new ClosingCountDto(t.ToString(), comptes[t])).ToList();
    }

    internal static IReadOnlyList<ClosingCountDto> ComptagesGravite(IReadOnlyCollection<ClosingAnomaly> anomalies) =>
        Gravites.Select(g => new ClosingCountDto(g.ToString(), anomalies.Count(a => a.Severity == g))).ToList();

    internal static (DateTime Debut, DateTime Fin) PeriodePrecedente(DateTime from, DateTime to)
    {
        var longueur = LongueurJours(from, to);
        var fin = from.Date.AddDays(-1);
        return (fin.AddDays(-(longueur - 1)), fin);
    }

    internal static int LongueurJours(DateTime from, DateTime to) => (to.Date - from.Date).Days + 1;

    internal static string? Departement(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();
}
