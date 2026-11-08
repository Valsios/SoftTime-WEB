using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class PayrollClosingRulesTests
{
    private static readonly DateTime Today = new(2025, 11, 20); // jeudi, hier = 2025-11-19
    private static readonly DateTime Lundi = new(2025, 11, 17);
    private static readonly DateTime Mardi = new(2025, 11, 18);
    private static readonly DateTime Mercredi = new(2025, 11, 19);
    private static readonly DateTime Samedi = new(2025, 11, 15);
    private static readonly DateTime Ferie = new(2025, 11, 11); // mardi
    private static readonly DateTime From = new(2025, 11, 10);
    private static readonly DateTime To = new(2025, 11, 30);

    private static readonly TimeSpan H8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan H17 = TimeSpan.FromHours(17);
    private static readonly TimeSpan H12 = TimeSpan.FromHours(12);

    private static CardPaie Carte(int id, string matricule) => new() { Id = id, SageMatricule = matricule };

    private static PlanningSalarie Plan(int id, int idsal, DateTime date, TimeSpan? ha = null, TimeSpan? hd = null, bool offP = false)
        => new()
        {
            Id = id,
            IdSal = idsal,
            DateP = date,
            Ha = ha.HasValue ? date.Date.Add(ha.Value) : null,
            Hd = hd.HasValue ? date.Date.Add(hd.Value) : null,
            OffP = offP
        };

    private static PlanningSalarie Travail(int id, int idsal, DateTime date) => Plan(id, idsal, date, H8, H17);

    private static PlanningSalarie Pause(int id, int idsal, DateTime date) => Plan(id, idsal, date, H12, H12);

    private static Pointage Punch(string matricule, DateTime date, TimeSpan heure)
        => new() { MatriculeSage = matricule, DatePointage = date, HeurePointage = heure };

    private static HeureCorrigee Corr(
        string matricule, DateTime date, string? origine = "AUTO", bool am = false, bool pm = false,
        TimeSpan? entree = null, TimeSpan? sortie = null, bool? ferier = null, decimal? mFeries = null)
        => new()
        {
            MatriculeSage = matricule,
            DatePointageIn = date,
            Origine = origine,
            AbsenceAm = am,
            AbsencePm = pm,
            HEntree = entree,
            HSortie = sortie,
            Ferier = ferier,
            MFeries = mFeries
        };

    private static Dictionary<string, List<LeaveDay>> Conges(string cle, params LeaveDay[] jours)
        => new() { [cle] = jours.ToList() };

    private static readonly Dictionary<string, List<LeaveDay>> SansConge = new();
    private static readonly HashSet<DateTime> SansFerie = new();
    private static readonly HashSet<DateTime> Feries = new() { Ferie };

    private static IReadOnlyList<ClosingAnomaly> Incomplets(
        IReadOnlyList<Pointage> pointages,
        IReadOnlyList<HeureCorrigee>? corrections = null,
        IReadOnlyList<HsValidee>? validees = null,
        IReadOnlyList<PlanningSalarie>? plannings = null,
        bool multi = false)
        => PunchCompletenessRules.PointagesIncomplets(
            new EmployeeSources(new[] { Carte(1, "00369") }, plannings ?? Array.Empty<PlanningSalarie>(), corrections ?? Array.Empty<HeureCorrigee>()),
            pointages,
            validees ?? Array.Empty<HsValidee>(),
            new ClosingScope(From, To, multi));

    private static IReadOnlyList<ClosingAnomaly> Manquants(
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<Pointage>? pointages = null,
        IReadOnlyList<HeureCorrigee>? corrections = null,
        IReadOnlyDictionary<string, List<LeaveDay>>? conges = null,
        IReadOnlySet<DateTime>? feries = null)
        => PunchCompletenessRules.PointagesManquants(
            new EmployeeSources(new[] { Carte(1, "00369") }, plannings, corrections ?? Array.Empty<HeureCorrigee>()),
            pointages ?? Array.Empty<Pointage>(),
            new AbsenceCalendar(conges ?? SansConge, feries ?? SansFerie),
            new ReportPeriod(From, To, Today));

    private static IReadOnlyList<ClosingAnomaly> SansPointage(
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<Pointage>? pointages = null,
        IReadOnlyList<HeureCorrigee>? corrections = null,
        IReadOnlyDictionary<string, List<LeaveDay>>? conges = null)
        => PlanningCoverageRules.PlanningsSansPointage(
            new EmployeeSources(new[] { Carte(1, "00369") }, plannings, corrections ?? Array.Empty<HeureCorrigee>()),
            pointages ?? Array.Empty<Pointage>(),
            new AbsenceCalendar(conges ?? SansConge, SansFerie),
            new ReportPeriod(From, To, Today));

    private static IReadOnlyList<ClosingAnomaly> FeriesNonAppliques(
        IReadOnlyList<PlanningSalarie> plannings,
        IReadOnlyList<HeureCorrigee>? corrections = null)
        => HolidayPunchRules.FeriesNonAppliques(
            new EmployeeSources(new[] { Carte(1, "00369") }, plannings, corrections ?? Array.Empty<HeureCorrigee>()),
            Feries,
            new ReportPeriod(From, To, Today));

    // ---------- Gravités par défaut ----------

    [Theory]
    [InlineData(ClosingAnomalyType.PointageIncomplet, ClosingSeverity.Critique)]
    [InlineData(ClosingAnomalyType.PointageManquant, ClosingSeverity.Critique)]
    [InlineData(ClosingAnomalyType.PlanningSansPointage, ClosingSeverity.Importante)]
    [InlineData(ClosingAnomalyType.PointageSansPlanning, ClosingSeverity.Importante)]
    [InlineData(ClosingAnomalyType.FerieNonApplique, ClosingSeverity.Information)]
    [InlineData(ClosingAnomalyType.DoublonExact, ClosingSeverity.Mineure)]
    [InlineData(ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure)]
    public void DefaultSeverity_MatchesSpecification(ClosingAnomalyType type, ClosingSeverity attendu)
    {
        PayrollClosingRules.GraviteParDefaut(type).Should().Be(attendu);
    }

    [Fact]
    public void Options_Defaults_AreToleranceOf30sAndSeverityWeights()
    {
        var options = ClosingRulesOptions.Default;

        options.DuplicateToleranceSeconds.Should().Be(30);
        options.Poids(ClosingSeverity.Information).Should().Be(0);
        options.Poids(ClosingSeverity.Mineure).Should().Be(1);
        options.Poids(ClosingSeverity.Importante).Should().Be(3);
        options.Poids(ClosingSeverity.Critique).Should().Be(5);
        options.PoidsMaximum.Should().Be(5);
    }

    // ---------- 3.1 PointageIncomplet ----------

    [Fact]
    public void Incomplete_SinglePunch_IsCriticalAnomalyWithCanonicalMatricule()
    {
        var result = Incomplets(new[] { Punch("369", Lundi, H8) });

        var anomalie = result.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(ClosingAnomalyType.PointageIncomplet);
        anomalie.Severity.Should().Be(ClosingSeverity.Critique);
        anomalie.Matricule.Should().Be("369");
        anomalie.Date.Should().Be(Lundi);
    }

    [Fact]
    public void Incomplete_EvenPunches_NoAnomaly()
    {
        var result = Incomplets(new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H17) });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Incomplete_MultiPunchOddCount_IsAnomaly()
    {
        var result = Incomplets(
            new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H12), Punch("369", Lundi, H17) },
            plannings: new[] { Travail(1, 1, Lundi) },
            multi: true);

        result.Should().ContainSingle().Which.Type.Should().Be(ClosingAnomalyType.PointageIncomplet);
    }

    [Fact]
    public void Incomplete_MultiPunchFourPunches_NoAnomaly()
    {
        var result = Incomplets(
            new[]
            {
                Punch("369", Lundi, H8), Punch("369", Lundi, H12),
                Punch("369", Lundi, TimeSpan.FromHours(13)), Punch("369", Lundi, H17)
            },
            plannings: new[] { Travail(1, 1, Lundi) },
            multi: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Incomplete_ValidatedWeek_IsIgnored()
    {
        var validee = new HsValidee { Matricules = "00369", DateDeb = Lundi, DateFin = Lundi.AddDays(6), ValidationHs = true };

        var result = Incomplets(new[] { Punch("369", Mercredi, H8) }, validees: new[] { validee });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Incomplete_UnvalidatedWeekRow_IsNotIgnored()
    {
        var nonValidee = new HsValidee { Matricules = "369", DateDeb = Lundi, ValidationHs = false };

        var result = Incomplets(new[] { Punch("369", Mercredi, H8) }, validees: new[] { nonValidee });

        result.Should().ContainSingle();
    }

    [Theory]
    [InlineData("MANUEL")]
    [InlineData(null)]
    public void Incomplete_DayWithManualOrLegacyCorrection_IsIgnored(string? origine)
    {
        var result = Incomplets(
            new[] { Punch("369", Lundi, H8) },
            corrections: new[] { Corr("369", Lundi, origine, entree: H8, sortie: H17) });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Incomplete_DayWithAutoCorrectionOnly_IsStillControlled()
    {
        var result = Incomplets(
            new[] { Punch("369", Lundi, H8) },
            corrections: new[] { Corr("369", Lundi, "AUTO", entree: H8, sortie: H17) });

        result.Should().ContainSingle();
    }

    // ---------- 3.2 PointageManquant ----------

    [Fact]
    public void Missing_PlannedDayWithoutPunchOrCorrection_IsCriticalWithPlannedHours()
    {
        var result = Manquants(new[] { Travail(1, 1, Lundi) });

        var anomalie = result.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(ClosingAnomalyType.PointageManquant);
        anomalie.Severity.Should().Be(ClosingSeverity.Critique);
        anomalie.Matricule.Should().Be("369");
        anomalie.Date.Should().Be(Lundi);
        anomalie.Heures.Should().Be(9m);
    }

    [Fact]
    public void Missing_PlannedDayWithCorrection_NoAnomaly()
    {
        var result = Manquants(
            new[] { Travail(1, 1, Lundi) },
            corrections: new[] { Corr("369", Lundi, entree: H8, sortie: H17) });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Missing_PlannedDayWithRawPunchOnly_NoAnomaly()
    {
        var result = Manquants(new[] { Travail(1, 1, Lundi) }, pointages: new[] { Punch("369", Lundi, H8) });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Missing_ExcludedDays_WeekendHolidayOffLeaveFuture_NoAnomaly()
    {
        var jeudiFutur = Today; // aujourd'hui n'est pas encore clos
        var plannings = new[]
        {
            Travail(1, 1, Samedi),
            Travail(2, 1, Ferie),
            Plan(3, 1, Lundi, H8, H17, offP: true),
            Travail(4, 1, Mardi),
            Travail(5, 1, jeudiFutur),
            Travail(6, 1, Today.AddDays(1))
        };

        var result = Manquants(
            plannings,
            conges: Conges("369", new LeaveDay(Mardi, 1m, "CP")),
            feries: Feries);

        result.Should().BeEmpty();
    }

    // ---------- 3.3 PlanningSansPointage ----------

    [Fact]
    public void PlannedWithoutPunch_HalfDayLeaveAndNoPunch_IsImportantWithHalfHours()
    {
        var result = SansPointage(
            new[] { Travail(1, 1, Lundi) },
            conges: Conges("369", new LeaveDay(Lundi, 0.5m, "CP")));

        var anomalie = result.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(ClosingAnomalyType.PlanningSansPointage);
        anomalie.Severity.Should().Be(ClosingSeverity.Importante);
        anomalie.Heures.Should().Be(4.5m);
    }

    [Fact]
    public void PlannedWithoutPunch_HalfDayArbitratedAbsenceAndNoHours_IsAnomaly()
    {
        var result = SansPointage(
            new[] { Travail(1, 1, Lundi) },
            corrections: new[] { Corr("369", Lundi, "MANUEL", am: true) });

        result.Should().ContainSingle().Which.Type.Should().Be(ClosingAnomalyType.PlanningSansPointage);
    }

    [Fact]
    public void PlannedWithoutPunch_HalfDayLeaveButOtherHalfPunched_NoAnomaly()
    {
        var parPointage = SansPointage(
            new[] { Travail(1, 1, Lundi) },
            pointages: new[] { Punch("369", Lundi, H12) },
            conges: Conges("369", new LeaveDay(Lundi, 0.5m, "CP")));
        var parCorrection = SansPointage(
            new[] { Travail(1, 1, Lundi) },
            corrections: new[] { Corr("369", Lundi, "MANUEL", am: true, entree: H12, sortie: H17) });

        parPointage.Should().BeEmpty();
        parCorrection.Should().BeEmpty();
    }

    [Fact]
    public void PlannedWithoutPunch_FullDayLeaveOrFullArbitration_NoAnomaly()
    {
        var conge = SansPointage(new[] { Travail(1, 1, Lundi) }, conges: Conges("369", new LeaveDay(Lundi, 1m, "CP")));
        var arbitrage = SansPointage(new[] { Travail(1, 1, Lundi) }, corrections: new[] { Corr("369", Lundi, am: true, pm: true) });

        conge.Should().BeEmpty();
        arbitrage.Should().BeEmpty();
    }

    [Fact]
    public void PlannedWithoutPunch_AndMissing_AreNeverBothRaisedForSameDay()
    {
        var plannings = new[] { Travail(1, 1, Lundi), Travail(2, 1, Mardi) };
        var conges = Conges("369", new LeaveDay(Mardi, 0.5m, "CP"));

        var manquants = Manquants(plannings, conges: conges);
        var sansPointage = SansPointage(plannings, conges: conges);

        manquants.Should().ContainSingle().Which.Date.Should().Be(Lundi);
        sansPointage.Should().ContainSingle().Which.Date.Should().Be(Mardi);
    }

    // ---------- 3.4 PointageSansPlanning ----------

    [Fact]
    public void PunchWithoutPlanning_IsImportantAnomaly()
    {
        // Salarié planifié un autre jour de la période : il est contrôlé, le jour pointé sans planning est signalé.
        var result = PlanningCoverageRules.PointagesSansPlanning(
            new[] { Carte(1, "00369") },
            new[] { Travail(1, 1, Mardi) },
            new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H17) },
            new ClosingScope(From, To));

        var anomalie = result.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(ClosingAnomalyType.PointageSansPlanning);
        anomalie.Severity.Should().Be(ClosingSeverity.Importante);
        anomalie.Date.Should().Be(Lundi);
    }

    [Fact]
    public void PunchWithAnyPlanningLine_IncludingOffAndPause_NoAnomaly()
    {
        var result = PlanningCoverageRules.PointagesSansPlanning(
            new[] { Carte(1, "00369"), Carte(2, "369") },
            new[] { Travail(1, 1, Lundi), Plan(2, 1, Mardi, offP: true), Pause(3, 2, Mercredi) },
            new[] { Punch("369", Lundi, H8), Punch("369", Mardi, H8), Punch("369", Mercredi, H8) },
            new ClosingScope(From, To));

        result.Should().BeEmpty();
    }

    // ---------- 3.5 DoublonExact / 3.6 QuasiDoublon ----------

    [Fact]
    public void ExactDuplicate_SameMatriculeDateAndTime_IsOneMinorAnomaly()
    {
        var result = DuplicatePunchRules.DoublonsExacts(
            new[] { Punch("369", Lundi, H8), Punch("00369", Lundi, H8), Punch("369", Lundi, H8) },
            new ClosingScope(From, To));

        var anomalie = result.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(ClosingAnomalyType.DoublonExact);
        anomalie.Severity.Should().Be(ClosingSeverity.Mineure);
        anomalie.Detail.Should().Contain("3");
    }

    [Fact]
    public void ExactDuplicate_DifferentTimesOrMatricules_NoAnomaly()
    {
        var result = DuplicatePunchRules.DoublonsExacts(
            new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H8.Add(TimeSpan.FromSeconds(1))), Punch("370", Lundi, H8) },
            new ClosingScope(From, To));

        result.Should().BeEmpty();
    }

    [Fact]
    public void NearDuplicate_29Seconds_IsAnomaly()
    {
        var result = DuplicatePunchRules.QuasiDoublons(
            new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H8.Add(TimeSpan.FromSeconds(29))) },
            new ClosingScope(From, To));

        var anomalie = result.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(ClosingAnomalyType.QuasiDoublon);
        anomalie.Severity.Should().Be(ClosingSeverity.Mineure);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void NearDuplicate_AtOrAboveTolerance_NoAnomaly(int secondes)
    {
        var result = DuplicatePunchRules.QuasiDoublons(
            new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H8.Add(TimeSpan.FromSeconds(secondes))) },
            new ClosingScope(From, To));

        result.Should().BeEmpty();
    }

    [Fact]
    public void NearDuplicate_CustomTolerance_IsApplied()
    {
        var options = new ClosingRulesOptions { DuplicateToleranceSeconds = 300 };

        var result = DuplicatePunchRules.QuasiDoublons(
            new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H8.Add(TimeSpan.FromSeconds(200))) },
            new ClosingScope(From, To, Options: options));

        result.Should().ContainSingle();
    }

    [Fact]
    public void ExactDuplicate_IsNotCountedAsNearDuplicate()
    {
        var pointages = new[] { Punch("369", Lundi, H8), Punch("369", Lundi, H8), Punch("369", Lundi, H17) };

        var exacts = DuplicatePunchRules.DoublonsExacts(pointages, new ClosingScope(From, To));
        var quasi = DuplicatePunchRules.QuasiDoublons(pointages, new ClosingScope(From, To));

        exacts.Should().ContainSingle();
        quasi.Should().BeEmpty();
    }

    [Fact]
    public void ExactAndNearDuplicate_SameMorning_EachCountedOnce()
    {
        var pointages = new[]
        {
            Punch("369", Lundi, H8), Punch("369", Lundi, H8),
            Punch("369", Lundi, H8.Add(TimeSpan.FromSeconds(20)))
        };

        DuplicatePunchRules.DoublonsExacts(pointages, new ClosingScope(From, To)).Should().ContainSingle();
        DuplicatePunchRules.QuasiDoublons(pointages, new ClosingScope(From, To)).Should().ContainSingle();
    }

    // ---------- 3.7 FerieNonApplique ----------

    [Fact]
    public void HolidayNotApplied_PlannedHolidayWithoutHolidayCorrection_IsInformation()
    {
        var result = FeriesNonAppliques(new[] { Travail(1, 1, Ferie) });

        var anomalie = result.Should().ContainSingle().Which;
        anomalie.Type.Should().Be(ClosingAnomalyType.FerieNonApplique);
        anomalie.Severity.Should().Be(ClosingSeverity.Information);
        anomalie.Date.Should().Be(Ferie);
        anomalie.Heures.Should().Be(9m);
    }

    [Fact]
    public void HolidayNotApplied_CorrectionWithoutHolidayFlag_IsStillAnomaly()
    {
        var result = FeriesNonAppliques(
            new[] { Travail(1, 1, Ferie) },
            new[] { Corr("369", Ferie, entree: H8, sortie: H17) });

        result.Should().ContainSingle();
    }

    [Fact]
    public void HolidayNotApplied_FerierFlagOrHolidayHours_NoAnomaly()
    {
        var parDrapeau = FeriesNonAppliques(new[] { Travail(1, 1, Ferie) }, new[] { Corr("369", Ferie, ferier: true) });
        var parHeures = FeriesNonAppliques(new[] { Travail(1, 1, Ferie) }, new[] { Corr("369", Ferie, mFeries: 8m) });

        parDrapeau.Should().BeEmpty();
        parHeures.Should().BeEmpty();
    }

    [Fact]
    public void HolidayNotApplied_OffDayOrNonHoliday_NoAnomaly()
    {
        var result = FeriesNonAppliques(new[] { Plan(1, 1, Ferie, H8, H17, offP: true), Travail(2, 1, Lundi) });

        result.Should().BeEmpty();
    }

    // ---------- 3.8 Règle Pause ----------

    [Fact]
    public void PauseShift_HaEqualsHd_WithoutPunch_NeverRaisesAnyAnomaly()
    {
        var plannings = new[] { Pause(1, 1, Lundi), Pause(2, 1, Ferie), Pause(3, 1, Mardi) };
        var conges = Conges("369", new LeaveDay(Mardi, 0.5m, "CP"));
        var cartes = new[] { Carte(1, "00369") };
        var aucunPointage = Array.Empty<Pointage>();
        var aucuneCorrection = Array.Empty<HeureCorrigee>();

        var toutes = new List<ClosingAnomaly>();
        toutes.AddRange(PunchCompletenessRules.PointagesIncomplets(new EmployeeSources(cartes, plannings, aucuneCorrection), aucunPointage, Array.Empty<HsValidee>(), new ClosingScope(From, To, true)));
        toutes.AddRange(PunchCompletenessRules.PointagesManquants(new EmployeeSources(cartes, plannings, aucuneCorrection), aucunPointage, new AbsenceCalendar(conges, Feries), new ReportPeriod(From, To, Today)));
        toutes.AddRange(PlanningCoverageRules.PlanningsSansPointage(new EmployeeSources(cartes, plannings, aucuneCorrection), aucunPointage, new AbsenceCalendar(conges, Feries), new ReportPeriod(From, To, Today)));
        toutes.AddRange(PlanningCoverageRules.PointagesSansPlanning(cartes, plannings, aucunPointage, new ClosingScope(From, To)));
        toutes.AddRange(DuplicatePunchRules.DoublonsExacts(aucunPointage, new ClosingScope(From, To)));
        toutes.AddRange(DuplicatePunchRules.QuasiDoublons(aucunPointage, new ClosingScope(From, To)));
        toutes.AddRange(HolidayPunchRules.FeriesNonAppliques(new EmployeeSources(cartes, plannings, aucuneCorrection), Feries, new ReportPeriod(From, To, Today)));

        toutes.Should().BeEmpty();
    }

    // ---------- 3.9 Score ----------

    private static ClosingAnomaly A(ClosingSeverity gravite) =>
        new(ClosingAnomalyType.PointageManquant, gravite, "369", Lundi, "test");

    [Fact]
    public void Score_NoAnomaly_Is100()
    {
        PayrollClosingRules.Score(Array.Empty<ClosingAnomaly>(), 20).Should().Be(100);
    }

    [Fact]
    public void Score_FixedSet_MatchesHandComputation()
    {
        // Poids : 5 + 3 + 1 + 1 = 10 ; 100 − 100 × 10 / (5 × 10) = 80
        var anomalies = new[] { A(ClosingSeverity.Critique), A(ClosingSeverity.Importante), A(ClosingSeverity.Mineure), A(ClosingSeverity.Mineure) };

        PayrollClosingRules.Score(anomalies, 10).Should().Be(80);
    }

    [Fact]
    public void Score_IsRoundedToNearestInteger()
    {
        // 100 − 100 × 3 / (5 × 7) = 91,43 → 91
        PayrollClosingRules.Score(new[] { A(ClosingSeverity.Importante) }, 7).Should().Be(91);
    }

    [Fact]
    public void Score_IsBoundedAtZero()
    {
        var anomalies = Enumerable.Range(0, 10).Select(_ => A(ClosingSeverity.Critique)).ToList();

        PayrollClosingRules.Score(anomalies, 2).Should().Be(0);
    }

    [Fact]
    public void Score_NoControlledDay_IsNullNot100()
    {
        PayrollClosingRules.Score(Array.Empty<ClosingAnomaly>(), 0).Should().BeNull();
    }

    [Fact]
    public void Score_CustomWeights_AreUsed()
    {
        // Poids max 10, une Critique pesant 10 sur 5 jours : 100 − 100 × 10 / 50 = 80
        var options = new ClosingRulesOptions { PoidsCritique = 10 };

        PayrollClosingRules.Score(new[] { A(ClosingSeverity.Critique) }, 5, options).Should().Be(80);
    }

    [Fact]
    public void Score_FormulaText_IsExposed()
    {
        PayrollClosingRules.FormuleScore.Should().Contain("poids maximum");
        PayrollClosingRules.FormuleScore.Should().Contain("jours-employé");
    }

    // ---------- Déterminisme ----------

    [Fact]
    public void Rules_SameInputShuffled_ProduceSameOrder()
    {
        var pointages = new List<Pointage>
        {
            Punch("500", Mardi, H8), Punch("500", Mardi, H8),
            Punch("369", Mardi, H8), Punch("369", Mardi, H8),
            Punch("369", Lundi, H17), Punch("369", Lundi, H17),
            Punch("500", Lundi, H8), Punch("500", Lundi, H8.Add(TimeSpan.FromSeconds(10))),
            Punch("500", Lundi, H8.Add(TimeSpan.FromSeconds(20)))
        };
        var inverses = Enumerable.Reverse(pointages).ToList();

        IReadOnlyList<ClosingAnomaly> Tout(IReadOnlyList<Pointage> p) => PayrollClosingRules.Trier(
            DuplicatePunchRules.DoublonsExacts(p, new ClosingScope(From, To)).Concat(DuplicatePunchRules.QuasiDoublons(p, new ClosingScope(From, To))));

        var premier = Tout(pointages);
        var second = Tout(inverses);

        second.Should().Equal(premier);
        premier.Should().Equal(premier.OrderBy(a => a.Date).ThenBy(a => a.Matricule, StringComparer.Ordinal).ThenBy(a => a.Type).ToList());
        premier.Select(a => a.Matricule).Should().Equal(new[] { "369", "500", "500", "369", "500" });
    }

    [Fact]
    public void Missing_TwoRuns_SameOutputSequence()
    {
        var cartes = new[] { Carte(2, "500"), Carte(1, "00369") };
        var plannings = new[] { Travail(4, 2, Mardi), Travail(3, 1, Mardi), Travail(2, 2, Lundi), Travail(1, 1, Lundi) };

        var premier = PunchCompletenessRules.PointagesManquants(new EmployeeSources(cartes, plannings, Array.Empty<HeureCorrigee>()), Array.Empty<Pointage>(), new AbsenceCalendar(SansConge, SansFerie), new ReportPeriod(From, To, Today));
        var second = PunchCompletenessRules.PointagesManquants(new EmployeeSources(cartes.Reverse().ToArray(), plannings.Reverse().ToArray(), Array.Empty<HeureCorrigee>()), Array.Empty<Pointage>(), new AbsenceCalendar(SansConge, SansFerie), new ReportPeriod(From, To, Today));

        second.Should().Equal(premier);
        premier.Select(a => (a.Date, a.Matricule)).Should().Equal(new[] { (Lundi, "369"), (Lundi, "500"), (Mardi, "369"), (Mardi, "500") });
    }
}
