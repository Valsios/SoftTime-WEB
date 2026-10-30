using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public sealed record MlDatasetBuildResult(int DailyRows, int WeeklyRows, DateTime From, DateTime To);

/// <summary>
/// Construction de la base analytique « model-ready » à partir des tables fonctionnelles.
///
/// Le principe est le même que les états : les labels sont produits par les règles existantes
/// (<see cref="AbsenceReportRules"/>, <see cref="OvertimeCalculator"/>), jamais par une
/// réimplémentation. Les features sont calculées avec les données connues avant la date cible
/// (as-of J-1) ; le planning du jour et les fériés sont autorisés car connus à l'avance.
/// </summary>
public sealed class MlDatasetService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly SageLeaveCalendar _leaves;
    private readonly IMlStore _store;
    private readonly MlOptions _options;
    private readonly ILogger<MlDatasetService> _logger;

    public MlDatasetService(
        IUnitOfWork uow,
        TenantConnectionService tenant,
        SageLeaveCalendar leaves,
        IMlStore store,
        MlOptions options,
        ILogger<MlDatasetService> logger)
    {
        _uow = uow;
        _tenant = tenant;
        _leaves = leaves;
        _store = store;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Reconstruit les lignes jour/semaine de la plage. <paramref name="persist"/> à false permet
    /// de réutiliser exactement le même calcul pour l'inférence (planning futur, sans écriture).
    /// </summary>
    public async Task<MlDatasetBuildResult> BuildAsync(
        DateTime from, DateTime to, bool persist, CancellationToken ct = default)
    {
        from = from.Date;
        to = to.Date;
        if (to < from)
            throw new InvalidOperationException("La date de fin doit suivre la date de début.");

        var ctx = await LoadContextAsync(from, to, ct);
        BuildDays(ctx);
        ComputeRollingFeatures(ctx);
        ComputeTeamFeatures(ctx);

        var daily = ctx.EmployeeDays.Values
            .SelectMany(days => days)
            .Where(d => d.Date >= from && d.Date <= to)
            .Select(d => ToDailyEntity(ctx, d))
            .ToList();

        var weekly = BuildWeekly(ctx, from, to);

        if (persist)
        {
            await _store.ReplaceDailyAsync(ctx.SageDb, from, to, daily, ct);
            await _store.ReplaceWeeklyAsync(ctx.SageDb, from, to, weekly, ct);
        }

        _logger.LogInformation(
            "ML dataset {Sage} : {Daily} jour(s), {Weekly} semaine(s) ({From:yyyy-MM-dd} → {To:yyyy-MM-dd}, persist={Persist}).",
            ctx.SageDb, daily.Count, weekly.Count, from, to, persist);

        return new MlDatasetBuildResult(daily.Count, weekly.Count, from, to);
    }

    /// <summary>Lignes d'inférence (planning futur) réutilisées par <see cref="MlPredictionService"/>.</summary>
    public async Task<IReadOnlyList<T_ML_SALARIE_JOUR>> BuildInferenceDailyAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var ctx = await LoadContextAsync(from.Date, to.Date, ct);
        BuildDays(ctx);
        ComputeRollingFeatures(ctx);
        ComputeTeamFeatures(ctx);
        return ctx.EmployeeDays.Values
            .SelectMany(days => days)
            .Where(d => d.Date >= from.Date && d.Date <= to.Date && d.EstJourPlanifie)
            .OrderBy(d => d.Matricule).ThenBy(d => d.Date)
            .Select(d => ToDailyEntity(ctx, d))
            .ToList();
    }

    /// <summary>Lignes d'inférence hebdomadaires (semaines futures).</summary>
    public async Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> BuildInferenceWeeklyAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var ctx = await LoadContextAsync(from.Date, to.Date, ct);
        BuildDays(ctx);
        ComputeRollingFeatures(ctx);
        ComputeTeamFeatures(ctx);
        return BuildWeekly(ctx, from.Date, to.Date);
    }

    // ------------------------------------------------------------------ contexte

    private async Task<DatasetContext> LoadContextAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var warmupFrom = from.AddDays(-Math.Max(0, _options.WarmupDays));

        var cards = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct))
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_MATRICULE))
            .Where(c => string.IsNullOrWhiteSpace(c.POINTEUSE_BDD)
                        || string.Equals(c.POINTEUSE_BDD.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => MlKey(c.SAGE_MATRICULE))
            .Select(g => g.OrderBy(c => c.ID).First())
            .ToList();

        var categories = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == sageDb, ct);
        var affectations = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct);
        var tolerances = await _uow.Repository<T_TOLERANCE>().ListAsync(_ => true, ct);
        var majorations = await _uow.Repository<T_MAJORATION>().ListAsync(_ => true, ct);
        var feries = (await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sageDb, ct))
            .Where(f => f.DATE.HasValue)
            .Select(f => f.DATE!.Value.Date)
            .ToHashSet();
        var clock = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault();
        var multiPointage = clock?.MULTIPOINT ?? true;

        var corrections = (await _uow.Repository<T_HEURECORRIGER>().ListAsync(
                h => h.BDD_SAGE == sageDb
                     && h.MATRICULE_SAGE != null
                     && h.DATE_POINTAGE_IN >= warmupFrom
                     && h.DATE_POINTAGE_IN <= to, ct))
            .GroupBy(h => MlKey(h.MATRICULE_SAGE))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var planning = (await _uow.Repository<T_PLANNING_SAL>().ListAsync(
                p => p.BDD_SAGE == sageDb && p.DateP >= warmupFrom && p.DateP <= to, ct))
            .Where(p => p.DateP.HasValue)
            .GroupBy(p => p.IDSAL)
            .ToDictionary(g => g.Key, g => g.ToList());

        var conges = await _leaves.LoadAsync(ct);

        var validees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                v => v.BDD_SAGE == sageDb
                     && v.VALIDATION_HS == true
                     && v.DATE_DEB != null
                     && v.DATE_DEB >= from.AddDays(-7)
                     && v.DATE_DEB <= to, ct))
            .Where(v => v.DATE_DEB.HasValue && !string.IsNullOrWhiteSpace(v.MATRICULES))
            .GroupBy(v => (MlKey(v.MATRICULES), v.DATE_DEB!.Value.Date))
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.ID).First());

        var ctx = new DatasetContext
        {
            SageDb = sageDb,
            PointeuseDb = pointeuseDb,
            From = from,
            To = to,
            WarmupFrom = warmupFrom,
            MultiPointage = multiPointage,
            Cards = cards,
            Categories = categories,
            Affectations = affectations,
            Tolerances = tolerances,
            Majorations = majorations,
            Feries = feries,
            Corrections = corrections,
            Planning = planning,
            Conges = conges,
            HsValidees = validees
        };

        foreach (var card in cards)
        {
            var cle = MlKey(card.SAGE_MATRICULE);
            ctx.CardByKey[cle] = card;
            ctx.Configs[cle] = BuildConfig(ctx, card);
            ctx.EmployeeDays[cle] = new List<DayRow>();
        }

        return ctx;
    }

    private static EmployeeConfig BuildConfig(DatasetContext ctx, T_CARDPAIE card)
    {
        var affectation = ctx.Affectations.FirstOrDefault(a => a.MATRICULE_SAGE == card.ID);
        var categorie = affectation == null
            ? null
            : ctx.Categories.FirstOrDefault(c => c.IDCATEGORIE == affectation.ID_CATEG);
        var tolerance = categorie == null
            ? null
            : ctx.Tolerances.FirstOrDefault(t => t.IDCATEGORIE == categorie.IDCATEGORIE && !t.TYPES_TOL);

        var week = new OvertimeWeekConfig(
            MultiPointage: ctx.MultiPointage,
            HeuresSemaine: categorie?.HEURESEMAINE ?? OvertimeRules.DefaultWeeklyHours,
            PauseCategorie: categorie?.PAUSE,
            ToleranceEntreeMinutes: tolerance?.TOLERANCE ?? 0,
            PasSortieMinutes: tolerance?.SORTIE,
            CotationNuit: Cotation(ctx.Majorations, "NUIT"),
            CotationDimanche: Cotation(ctx.Majorations, "DIMANCHE"),
            CotationFerie: Cotation(ctx.Majorations, "FERIE"),
            EstFemme: false);

        return new EmployeeConfig(week, categorie?.IDCATEGORIE, categorie?.HEURESEMAINE);
    }

    private static decimal Cotation(IReadOnlyList<T_MAJORATION> majorations, string type)
        => majorations
            .Where(m => string.Equals(m.Mojoration?.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Cotation ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();

    // ------------------------------------------------------------------ jours

    private static void BuildDays(DatasetContext ctx)
    {
        foreach (var card in ctx.Cards)
        {
            var cle = MlKey(card.SAGE_MATRICULE);
            var config = ctx.Configs[cle];
            var conges = ctx.Conges.GetValueOrDefault(cle) ?? new List<LeaveDay>();
            var corrections = ctx.Corrections.GetValueOrDefault(cle) ?? new List<T_HEURECORRIGER>();
            var correctionsParJour = corrections
                .Where(h => h.DATE_POINTAGE_IN.HasValue)
                .GroupBy(h => h.DATE_POINTAGE_IN!.Value.Date)
                .ToDictionary(g => g.Key, g => g.ToList());
            var plansParJour = (ctx.Planning.GetValueOrDefault(card.ID) ?? new List<T_PLANNING_SAL>())
                .Where(p => p.DateP.HasValue)
                .GroupBy(p => p.DateP!.Value.Date)
                .ToDictionary(g => g.Key, g => SelectPlan(g));

            var days = new List<DayRow>();
            for (var date = ctx.WarmupFrom; date <= ctx.To; date = date.AddDays(1))
            {
                plansParJour.TryGetValue(date, out var plan);
                correctionsParJour.TryGetValue(date, out var lignes);
                lignes ??= new List<T_HEURECORRIGER>();

                var off = AbsenceReportRules.EstJourOff(plan);
                var ferie = ctx.Feries.Contains(date);
                var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var congeValeur = LeaveRules.Valeur(conges, date);
                var conge = congeValeur > 0m;
                var dureePlanifiee = off ? null : AbsenceReportRules.CalculerDureeHeures(plan);

                var sentinelle = new DateTime(2000, 1, 1);
                TimeSpan? ha = plan?.HA is DateTime haBrute && haBrute.Date > sentinelle ? haBrute.TimeOfDay : null;
                TimeSpan? hd = plan?.HD is DateTime hdBrute && hdBrute.Date > sentinelle ? hdBrute.TimeOfDay : null;
                if (off)
                {
                    ha = null;
                    hd = null;
                }

                var paires = lignes
                    .Where(h => h.HENTREE.HasValue && h.HSORTIE.HasValue)
                    .OrderBy(h => h.HENTREE)
                    .Select(h => new PunchPair(
                        h.DATE_POINTAGE_IN!.Value.Date,
                        h.HENTREE!.Value,
                        (h.DATE_POINTAGE_OUT ?? h.DATE_POINTAGE_IN)!.Value.Date,
                        h.HSORTIE!.Value))
                    .ToList();

                if (!off && paires.Count > 0)
                {
                    ha ??= paires[0].EntreeHeure;
                    hd ??= paires[^1].SortieHeure;
                }

                var aPointage = lignes.Count > 0;
                var absenceNonJustifiee = AbsenceReportRules.EstNonJustifiee(
                    estNonTravaille: weekend || ferie,
                    estConge: conge,
                    plan,
                    aPointage);
                var absenceDeclaree = lignes.Any(h => h.ABSENCE_AM == true || h.ABSENCE_PM == true);
                var retardSaisi = lignes.FirstOrDefault(h => h.RETARD.HasValue)?.RETARD;
                var retardMinutes = retardSaisi is TimeSpan retard && retard > TimeSpan.Zero
                    ? (int)Math.Round(retard.TotalMinutes)
                    : 0;

                var day = new DayRow
                {
                    Matricule = cle,
                    Date = date,
                    Departement = card.DEPARTEMENT,
                    Off = off,
                    Ferie = ferie,
                    Weekend = weekend,
                    Conge = conge,
                    CongeValeur = congeValeur,
                    PlanningConnu = plan != null,
                    EstJourPlanifie = !off && !weekend && !ferie && !conge && dureePlanifiee.HasValue,
                    HeuresPlanifiees = dureePlanifiee,
                    HeuresTravaillees = HeuresTravaillees(paires),
                    RetardMinutes = retardMinutes,
                    AbsenceNonJustifiee = absenceNonJustifiee,
                    AbsenceDeclaree = absenceDeclaree,
                    AbsenceHeures = absenceNonJustifiee ? dureePlanifiee : null,
                    OvertimeDay = new OvertimeDayInput(
                        Jour: date,
                        Paires: paires,
                        PlanningHa: ha,
                        PlanningHd: hd,
                        PlanningPause: off ? null : plan?.Pause,
                        Off: off,
                        Ferie: ferie,
                        CorrectionPresente: aPointage,
                        CongeValeur: congeValeur,
                        RetardExplicite: retardSaisi)
                };

                days.Add(day);
            }

            ctx.EmployeeDays[cle] = days;

            // Résultats hebdomadaires : base des features HS et des labels.
            foreach (var groupe in days.GroupBy(d => MondayOf(d.Date)))
            {
                var result = OvertimeCalculator.CalculerSemaine(
                    config.Week,
                    groupe.OrderBy(d => d.Date).Select(d => d.OvertimeDay));

                if (ctx.HsValidees.TryGetValue((cle, groupe.Key), out var validee))
                {
                    ctx.WeekResults[(cle, groupe.Key)] = new WeekComputation(
                        validee.HS ?? 0m,
                        validee.NUIT ?? 0m,
                        validee.DIMANCHE ?? 0m,
                        validee.FERIES ?? 0m,
                        validee.RETARD ?? 0m,
                        validee.ABSENCE ?? 0m,
                        Validee: true);
                }
                else
                {
                    ctx.WeekResults[(cle, groupe.Key)] = new WeekComputation(
                        result.Hs, result.Nuit, result.Dimanche, result.Ferie,
                        result.Retard, result.Absence, Validee: false);
                }
            }
        }
    }

    private static T_PLANNING_SAL? SelectPlan(IEnumerable<T_PLANNING_SAL> plans)
        => plans
            .OrderBy(p => p.OffP == true ? 1 : 0)
            .ThenBy(p => p.ID)
            .FirstOrDefault();

    private static decimal HeuresTravaillees(IReadOnlyList<PunchPair> paires)
    {
        decimal total = 0m;
        foreach (var paire in paires)
        {
            var debut = paire.EntreeDate.Date + paire.EntreeHeure;
            var fin = paire.SortieDate.Date + paire.SortieHeure;
            if (fin < debut)
                fin = fin.AddDays(1);
            total += (decimal)(fin - debut).TotalHours;
        }
        return Math.Round(total, 2);
    }

    // ------------------------------------------------------------------ features

    private static void ComputeRollingFeatures(DatasetContext ctx)
    {
        foreach (var (cle, days) in ctx.EmployeeDays)
        {
            var ordered = days.OrderBy(d => d.Date).ToList();
            var history = new List<DayRow>();

            foreach (var day in ordered)
            {
                var debut7 = day.Date.AddDays(-7);
                var debut30 = day.Date.AddDays(-30);
                var debut90 = day.Date.AddDays(-90);

                var fenetre7 = history.Where(h => h.Date > debut7).ToList();
                var fenetre30 = history.Where(h => h.Date > debut30).ToList();
                var fenetre90 = history.Where(h => h.Date > debut90).ToList();

                day.Absences7j = fenetre7.Count(h => h.AbsenceNonJustifiee);
                day.Absences30j = fenetre30.Count(h => h.AbsenceNonJustifiee);
                day.Absences90j = fenetre90.Count(h => h.AbsenceNonJustifiee);
                var planifies30 = fenetre30.Count(h => h.EstJourPlanifie);
                day.TauxAbsence30j = planifies30 == 0 ? 0d : (double)day.Absences30j / planifies30;
                day.Retards30j = fenetre30.Count(h => h.RetardMinutes > 0);
                day.JoursTravailles7j = fenetre7.Count(h => h.HeuresTravaillees > 0m);

                var derniereAbsence = history
                    .Where(h => h.AbsenceNonJustifiee)
                    .OrderByDescending(h => h.Date)
                    .FirstOrDefault();
                day.JoursDepuisDerniereAbsence = derniereAbsence == null
                    ? null
                    : (day.Date - derniereAbsence.Date).Days;

                var lundi = MondayOf(day.Date);
                day.HeuresSupp4Semaines = Math.Round(
                    Enumerable.Range(1, 4)
                        .Sum(i => ctx.WeekResults
                            .GetValueOrDefault((cle, lundi.AddDays(-7 * i)))?.Hs ?? 0m),
                    2);

                history.Add(day);
            }
        }
    }

    private static void ComputeTeamFeatures(DatasetContext ctx)
    {
        var all = ctx.EmployeeDays.Values.SelectMany(d => d).ToList();

        foreach (var dept in all.GroupBy(d => d.Departement ?? string.Empty))
        {
            var parDate = dept.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.ToList());
            var dates = parDate.Keys.OrderBy(d => d).ToList();
            var planned = parDate.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count(d => d.EstJourPlanifie));
            var absents = parDate.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count(d => d.AbsenceNonJustifiee));

            var fenetre = new List<DateTime>();
            var plannedSum = 0;
            var absentsSum = 0;

            foreach (var date in dates)
            {
                while (fenetre.Count > 0 && fenetre[0] < date.AddDays(-30))
                {
                    plannedSum -= planned[fenetre[0]];
                    absentsSum -= absents[fenetre[0]];
                    fenetre.RemoveAt(0);
                }

                var taux = plannedSum == 0 ? 0d : (double)absentsSum / plannedSum;
                foreach (var row in parDate[date])
                    row.TauxAbsenceEquipe30j = taux;

                fenetre.Add(date);
                plannedSum += planned[date];
                absentsSum += absents[date];
            }
        }

        var parJour = all
            .GroupBy(d => d.Date)
            .ToDictionary(
                g => g.Key,
                g => (Planned: g.Count(d => d.EstJourPlanifie), Conges: g.Count(d => d.Conge)));

        foreach (var day in all)
        {
            if (parJour.TryGetValue(day.Date, out var stats))
            {
                day.EffectifPrevuJour = stats.Planned;
                day.CongesPrevusJour = stats.Conges;
            }
        }
    }

    // ------------------------------------------------------------------ semaines

    private static List<T_ML_SALARIE_SEMAINE> BuildWeekly(DatasetContext ctx, DateTime from, DateTime to)
    {
        var rows = new List<T_ML_SALARIE_SEMAINE>();

        foreach (var (cle, days) in ctx.EmployeeDays)
        {
            var card = ctx.CardByKey[cle];
            var config = ctx.Configs[cle];
            var semaines = days
                .GroupBy(d => MondayOf(d.Date))
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var semaine in semaines)
            {
                if (semaine.Key < from || semaine.Key > to)
                    continue;

                var computation = ctx.WeekResults[(cle, semaine.Key)];
                var jours = semaine.OrderBy(d => d.Date).ToList();
                var precedentes4 = semaines
                    .Where(w => w.Key < semaine.Key && w.Key >= semaine.Key.AddDays(-28))
                    .ToList();
                var precedentes8 = semaines
                    .Where(w => w.Key < semaine.Key && w.Key >= semaine.Key.AddDays(-56))
                    .ToList();

                decimal HsMoyenne(List<IGrouping<DateTime, DayRow>> semainesSource)
                    => semainesSource.Count == 0
                        ? 0m
                        : Math.Round(semainesSource.Average(w => ctx.WeekResults[(cle, w.Key)].Hs), 2);

                rows.Add(new T_ML_SALARIE_SEMAINE
                {
                    BddSage = ctx.SageDb,
                    Matricule = cle,
                    SemaineDebut = semaine.Key,
                    SemaineIso = (byte)System.Globalization.ISOWeek.GetWeekOfYear(semaine.Key),
                    AnneeIso = (short)System.Globalization.ISOWeek.GetYear(semaine.Key),
                    HeuresPlanifiees = Math.Round(jours.Sum(d => d.HeuresPlanifiees ?? 0m), 2),
                    HeuresTravaillees = Math.Round(jours.Sum(d => d.HeuresTravaillees), 2),
                    NbJoursPlanifies = jours.Count(d => d.EstJourPlanifie),
                    NbJoursTravailles = jours.Count(d => d.HeuresTravaillees > 0m),
                    CongeJours = Math.Round(jours.Sum(d => d.CongeValeur), 1),
                    HS = Math.Round(computation.Hs, 2),
                    HSValidee = computation.Validee,
                    Nuit = Math.Round(computation.Nuit, 2),
                    Dimanche = Math.Round(computation.Dimanche, 2),
                    Ferie = Math.Round(computation.Ferie, 2),
                    RetardMinutes = (int)Math.Round(computation.Retard),
                    AbsenceHeures = Math.Round(computation.Absence, 2),
                    HsMoyenne4Semaines = HsMoyenne(precedentes4),
                    HsMoyenne8Semaines = HsMoyenne(precedentes8),
                    Absences4Semaines = precedentes4.Sum(w => w.Count(d => d.AbsenceNonJustifiee)),
                    Retards4Semaines = precedentes4.Sum(w => w.Count(d => d.RetardMinutes > 0)),
                    HeuresTravaillees4Semaines = Math.Round(precedentes4.Sum(w => w.Sum(d => d.HeuresTravaillees)), 2),
                    CategorieId = config.CategorieId,
                    Departement = card.DEPARTEMENT,
                    HeuresSemaineContrat = config.HeuresSemaine,
                    MultiPointage = ctx.MultiPointage,
                    SourceGenereeLe = DateTime.Now,
                    VersionSchema = MlSchema.Version
                });
            }
        }

        return rows;
    }

    private static T_ML_SALARIE_JOUR ToDailyEntity(DatasetContext ctx, DayRow day)
    {
        var config = ctx.Configs[day.Matricule];
        var card = ctx.CardByKey[day.Matricule];

        return new T_ML_SALARIE_JOUR
        {
            BddSage = ctx.SageDb,
            Matricule = day.Matricule,
            Date = day.Date,
            EstJourPlanifie = day.EstJourPlanifie,
            EstOff = day.Off,
            EstFerie = day.Ferie,
            EstWeekend = day.Weekend,
            EstConge = day.Conge,
            PlanningConnu = day.PlanningConnu,
            HeuresPlanifiees = day.HeuresPlanifiees,
            HeuresTravaillees = day.HeuresTravaillees,
            RetardMinutes = day.RetardMinutes,
            AbsenceNonJustifiee = day.AbsenceNonJustifiee,
            AbsenceDeclaree = day.AbsenceDeclaree,
            AbsenceHeures = day.AbsenceHeures,
            JourSemaine = (byte)day.Date.DayOfWeek,
            SemaineIso = (byte)System.Globalization.ISOWeek.GetWeekOfYear(day.Date),
            AnneeIso = (short)System.Globalization.ISOWeek.GetYear(day.Date),
            Mois = (byte)day.Date.Month,
            JourDuMois = (byte)day.Date.Day,
            VeilleFerie = ctx.Feries.Contains(day.Date.AddDays(1)),
            LendemainFerie = ctx.Feries.Contains(day.Date.AddDays(-1)),
            DebutMois = day.Date.Day <= 3,
            FinMois = day.Date.Day >= DateTime.DaysInMonth(day.Date.Year, day.Date.Month) - 2,
            CategorieId = config.CategorieId,
            Departement = card.DEPARTEMENT,
            HeuresSemaineContrat = config.HeuresSemaine,
            MultiPointage = ctx.MultiPointage,
            AncienneteJours = card.DATE.HasValue ? (day.Date - card.DATE.Value.Date).Days : null,
            Absences7j = day.Absences7j,
            Absences30j = day.Absences30j,
            Absences90j = day.Absences90j,
            TauxAbsence30j = day.TauxAbsence30j,
            Retards30j = day.Retards30j,
            JoursTravailles7j = day.JoursTravailles7j,
            JoursDepuisDerniereAbsence = day.JoursDepuisDerniereAbsence,
            HeuresSupp4Semaines = day.HeuresSupp4Semaines,
            TauxAbsenceEquipe30j = day.TauxAbsenceEquipe30j,
            EffectifPrevuJour = day.EffectifPrevuJour,
            CongesPrevusJour = day.CongesPrevusJour,
            SourceGenereeLe = DateTime.Now,
            VersionSchema = MlSchema.Version
        };
    }

    // ------------------------------------------------------------------ utilitaires

    private static string MlKey(string? matricule) => MatriculeIdentity.Cle(matricule);

    private static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));

    private sealed class DatasetContext
    {
        public string SageDb { get; init; } = string.Empty;
        public string PointeuseDb { get; init; } = string.Empty;
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public DateTime WarmupFrom { get; init; }
        public bool MultiPointage { get; init; }
        public List<T_CARDPAIE> Cards { get; init; } = new();
        public List<T_CATEGORIE> Categories { get; init; } = new();
        public List<T_CAT_SAL> Affectations { get; init; } = new();
        public List<T_TOLERANCE> Tolerances { get; init; } = new();
        public List<T_MAJORATION> Majorations { get; init; } = new();
        public HashSet<DateTime> Feries { get; init; } = new();
        public Dictionary<string, List<T_HEURECORRIGER>> Corrections { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<int, List<T_PLANNING_SAL>> Planning { get; init; } = new();
        public Dictionary<string, List<LeaveDay>> Conges { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<(string, DateTime), T_HS_VALIDER> HsValidees { get; init; } = new();

        public Dictionary<string, T_CARDPAIE> CardByKey { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EmployeeConfig> Configs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<DayRow>> EmployeeDays { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string, DateTime), WeekComputation> WeekResults { get; } = new();
    }

    private sealed record EmployeeConfig(OvertimeWeekConfig Week, int? CategorieId, decimal? HeuresSemaine);

    private sealed record WeekComputation(
        decimal Hs, decimal Nuit, decimal Dimanche, decimal Ferie,
        decimal Retard, decimal Absence, bool Validee);

    private sealed class DayRow
    {
        public string Matricule { get; init; } = string.Empty;
        public DateTime Date { get; init; }
        public string? Departement { get; init; }
        public bool Off { get; init; }
        public bool Ferie { get; init; }
        public bool Weekend { get; init; }
        public bool Conge { get; init; }
        public decimal CongeValeur { get; init; }
        public bool PlanningConnu { get; init; }
        public bool EstJourPlanifie { get; init; }
        public decimal? HeuresPlanifiees { get; init; }
        public decimal HeuresTravaillees { get; init; }
        public int RetardMinutes { get; init; }
        public bool AbsenceNonJustifiee { get; init; }
        public bool AbsenceDeclaree { get; init; }
        public decimal? AbsenceHeures { get; init; }
        public OvertimeDayInput OvertimeDay { get; init; } = null!;

        public int Absences7j { get; set; }
        public int Absences30j { get; set; }
        public int Absences90j { get; set; }
        public double TauxAbsence30j { get; set; }
        public int Retards30j { get; set; }
        public int JoursTravailles7j { get; set; }
        public int? JoursDepuisDerniereAbsence { get; set; }
        public decimal HeuresSupp4Semaines { get; set; }
        public double TauxAbsenceEquipe30j { get; set; }
        public int EffectifPrevuJour { get; set; }
        public int CongesPrevusJour { get; set; }
    }
}
