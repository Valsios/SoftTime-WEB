using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class RetardReportRulesTests
{
    private static readonly DateTime Today = new(2025, 11, 20); // Hier = 2025-11-19
    private static readonly DateTime Mardi = new(2025, 11, 4);  // Jour ouvré de référence
    private static readonly DateTime Samedi = new(2025, 11, 8);

    private static T_CARDPAIE Carte(int id, string matricule, string? nom = "Dupont", string? prenom = "Jean", string? dep = "INFO")
        => new() { ID = id, SAGE_MATRICULE = matricule, SAGE_NOM = nom, SAGE_PRENOM = prenom, DEPARTEMENT = dep };

    private static T_PLANNING_SAL Plan(
        int id, int idsal, DateTime date, string? ha = "08:00", string? hd = "17:00", bool offP = false)
        => new()
        {
            ID = id,
            IDSAL = idsal,
            DateP = date,
            HA = ha is null ? null : date.Date.Add(TimeSpan.Parse(ha)),
            HD = hd is null ? null : date.Date.Add(TimeSpan.Parse(hd)),
            OffP = offP
        };

    private static T_HEURECORRIGER Corr(
        int id,
        string matricule,
        DateTime date,
        string? entree = "08:00",
        string? retard = null,
        bool absenceAm = false,
        bool absencePm = false)
        => new()
        {
            ID = id,
            MATRICULE_SAGE = matricule,
            DATE_POINTAGE_IN = date,
            DATE_POINTAGE_OUT = date,
            HENTREE = entree is null ? null : TimeSpan.Parse(entree),
            HSORTIE = TimeSpan.Parse("17:00"),
            ABSENCE_AM = absenceAm,
            ABSENCE_PM = absencePm,
            RETARD = retard is null ? null : TimeSpan.Parse(retard),
            VALIDER_CORRECTION = true
        };

    private static T_CAT_SAL Affectation(int carteId, int categorie)
        => new() { MATRICULE_SAGE = carteId, ID_CATEG = categorie };

    private static T_TOLERANCE Tolerance(int categorie, int minutes)
        => new() { IDCATEGORIE = categorie, TOLERANCE = minutes, TYPES_TOL = false };

    private static IReadOnlyList<RetardReportRow> Calculer(
        IReadOnlyList<T_CARDPAIE> cartes,
        IReadOnlyList<T_HEURECORRIGER> corrections,
        IReadOnlyList<T_PLANNING_SAL>? plannings = null,
        IReadOnlyList<T_CAT_SAL>? affectations = null,
        IReadOnlyList<T_TOLERANCE>? tolerances = null,
        IReadOnlySet<DateTime>? feries = null,
        DateTime? from = null,
        DateTime? to = null)
        => RetardReportRules.Calculer(
            cartes,
            corrections,
            plannings ?? Array.Empty<T_PLANNING_SAL>(),
            (affectations ?? Array.Empty<T_CAT_SAL>())
                .GroupBy(a => a.MATRICULE_SAGE)
                .ToDictionary(g => g.Key, g => g.First().ID_CATEG),
            tolerances ?? Array.Empty<T_TOLERANCE>(),
            feries ?? new HashSet<DateTime>(),
            from ?? Mardi,
            to ?? Mardi,
            Today);

    [Fact]
    public void Calculer_EntreeApresTolerance_RetourneLeRetardEnMinutes()
    {
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        var row = Assert.Single(result);
        Assert.Equal("001", row.Matricule);
        Assert.Equal("Dupont", row.Nom);
        Assert.Equal("Jean", row.Prenom);
        Assert.Equal("INFO", row.Departement);
        Assert.Equal(Mardi, row.Date);
        Assert.Equal(TimeSpan.Parse("08:45"), row.HeureEntree);
        Assert.Equal(TimeSpan.Parse("08:00"), row.HeurePrevue);
        Assert.Equal(10, row.ToleranceMinutes);
        Assert.Equal(35, row.MinutesRetard);
        Assert.False(row.RetardSaisi);
    }

    [Fact]
    public void Calculer_EntreeDansLaTolerance_AucuneLigne()
    {
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:05") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_SansPlanning_AucuneLigne()
    {
        // Le desktop retombait sur 00:00 : la référence est désormais le planning, comme le dashboard.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") });

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null, "17:00")]
    [InlineData("00:00", "00:00")]
    public void Calculer_HeuresDuPlanningNonExploitables_AucuneLigne(string? ha, string? hd)
    {
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") },
            new[] { Plan(1, 1, Mardi, ha, hd) });

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_JourOff_AucunRetard()
    {
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") },
            new[] { Plan(1, 1, Mardi, offP: true) });

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_WeekEndEtFerie_Exclus()
    {
        var ferie = new DateTime(2025, 11, 5);
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[]
            {
                Corr(1, "001", Samedi, entree: "08:45"),
                Corr(2, "001", ferie, entree: "08:45")
            },
            new[] { Plan(1, 1, Samedi), Plan(2, 1, ferie) },
            feries: new HashSet<DateTime> { ferie },
            from: Samedi,
            to: ferie);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_AbsenceMatin_ArriveeApresMidi_NEstPasUnRetard()
    {
        // Pas de retard « de midi » : la journée est couverte par une absence du matin.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "14:00", absenceAm: true) },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_DeuxPairesMemeJour_SeuleLaPremiereEntreeCompte()
    {
        // Multi-pointage : 08:30 (entrée) et 13:05 (2e paire) -> une seule ligne de 20 minutes.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[]
            {
                Corr(1, "001", Mardi, entree: "08:30"),
                Corr(2, "001", Mardi, entree: "13:05")
            },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        var row = Assert.Single(result);
        Assert.Equal(TimeSpan.Parse("08:30"), row.HeureEntree);
        Assert.Equal(20, row.MinutesRetard);
    }

    [Fact]
    public void Calculer_MonoUneSeulePaire_MemeResultat()
    {
        // Mono-pointage : une paire par jour, entrée 08:30 -> 20 minutes.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:30") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        var row = Assert.Single(result);
        Assert.Equal(20, row.MinutesRetard);
    }

    [Fact]
    public void Calculer_RetardSaisi_PrioritaireSurLeCalcul()
    {
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:00", retard: "00:42:00") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        var row = Assert.Single(result);
        Assert.Equal(42, row.MinutesRetard);
        Assert.True(row.RetardSaisi);
    }

    [Fact]
    public void Calculer_RetardSaisiAZero_AucuneLigne()
    {
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "09:30", retard: "00:00:00") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_CorrectionSansEntreeNiRetard_AucuneLigne()
    {
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: null) },
            new[] { Plan(1, 1, Mardi) });

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_JourFutur_EstExclu()
    {
        // Today = 20/11 : le 20 et le 21 ne sont jamais contrôlés.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Today, entree: "09:30"), Corr(2, "001", Today.AddDays(1), entree: "09:30") },
            new[] { Plan(1, 1, Today), Plan(2, 1, Today.AddDays(1)) },
            from: Today,
            to: Today.AddDays(5));

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_ToleranceParCategorie()
    {
        // Sans tolérance : 08:05 -> 5 minutes. Avec 10 minutes : aucune ligne.
        var sansTolerance = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:05") },
            new[] { Plan(1, 1, Mardi) });

        var avecTolerance = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:05") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        Assert.Equal(5, Assert.Single(sansTolerance).MinutesRetard);
        Assert.Empty(avecTolerance);
    }

    [Fact]
    public void Calculer_DeuxSalaries_TriParDatePuisMatricule()
    {
        var j1 = new DateTime(2025, 11, 4);
        var j2 = new DateTime(2025, 11, 5);
        var result = Calculer(
            new[] { Carte(1, "002"), Carte(2, "001") },
            new[]
            {
                Corr(1, "002", j2, entree: "09:00"),
                Corr(2, "001", j2, entree: "09:00"),
                Corr(3, "002", j1, entree: "09:00")
            },
            new[] { Plan(1, 1, j1), Plan(2, 1, j2), Plan(3, 2, j1), Plan(4, 2, j2) },
            from: j1,
            to: j2);

        Assert.Equal(3, result.Count);
        Assert.Equal(j1, result[0].Date);
        Assert.Equal("002", result[0].Matricule);
        Assert.Equal(j2, result[1].Date);
        Assert.Equal("001", result[1].Matricule);
        Assert.Equal(j2, result[2].Date);
        Assert.Equal("002", result[2].Matricule);
    }

    [Fact]
    public void MinutesRetard_JamaisNegatif()
    {
        Assert.Equal(0, RetardReportRules.MinutesRetard(TimeSpan.Parse("07:55"), TimeSpan.Parse("08:00"), 10));
        Assert.Equal(0, RetardReportRules.MinutesRetard(TimeSpan.Parse("08:05"), TimeSpan.Parse("08:00"), 10));
        Assert.Equal(35, RetardReportRules.MinutesRetard(TimeSpan.Parse("08:45"), TimeSpan.Parse("08:00"), 10));
    }

    [Fact]
    public void HeurePrevue_PlanningOffOuSentinelle_EstNulle()
    {
        Assert.Null(RetardReportRules.HeurePrevue(null));
        Assert.Null(RetardReportRules.HeurePrevue(Plan(1, 1, Mardi, offP: true)));
        Assert.Null(RetardReportRules.HeurePrevue(Plan(1, 1, Mardi, ha: null)));
        Assert.Null(RetardReportRules.HeurePrevue(new T_PLANNING_SAL
        {
            ID = 1,
            DateP = Mardi,
            HA = new DateTime(1800, 1, 1, 8, 0, 0)
        }));
        Assert.Equal(TimeSpan.Parse("08:00"), RetardReportRules.HeurePrevue(Plan(1, 1, Mardi)));
    }
}
