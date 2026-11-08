using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class RetardReportRulesTests
{
    private static readonly DateTime Today = new(2025, 11, 20); // Hier = 2025-11-19
    private static readonly DateTime Mardi = new(2025, 11, 4);  // Jour ouvré de référence
    private static readonly DateTime Samedi = new(2025, 11, 8);

    private static CardPaie Carte(int id, string matricule, string? nom = "Dupont", string? prenom = "Jean", string? dep = "INFO")
        => new() { Id = id, SageMatricule = matricule, SageNom = nom, SagePrenom = prenom, Departement = dep };

    private static PlanningSalarie Plan(
        int id, int idsal, DateTime date, string? ha = "08:00", string? hd = "17:00", bool offP = false)
        => new()
        {
            Id = id,
            IdSal = idsal,
            DateP = date,
            Ha = ha is null ? null : date.Date.Add(TimeSpan.Parse(ha)),
            Hd = hd is null ? null : date.Date.Add(TimeSpan.Parse(hd)),
            OffP = offP
        };

    private static HeureCorrigee Corr(
        int id,
        string matricule,
        DateTime date,
        string? entree = "08:00",
        string? retard = null,
        bool absenceAm = false,
        bool absencePm = false)
        => new()
        {
            Id = id,
            MatriculeSage = matricule,
            DatePointageIn = date,
            DatePointageOut = date,
            HEntree = entree is null ? null : TimeSpan.Parse(entree),
            HSortie = TimeSpan.Parse("17:00"),
            AbsenceAm = absenceAm,
            AbsencePm = absencePm,
            Retard = retard is null ? null : TimeSpan.Parse(retard),
            ValiderCorrection = true
        };

    private static CategorieSalarie Affectation(int carteId, int categorie)
        => new() { MatriculeSage = carteId, IdCateg = categorie };

    private static ToleranceRetard Tolerance(int categorie, int minutes)
        => new() { IdCategorie = categorie, Tolerance = minutes, TypesTol = false };

    private static IReadOnlyList<RetardReportRow> Calculer(
        IReadOnlyList<CardPaie> cartes,
        IReadOnlyList<HeureCorrigee> corrections,
        IReadOnlyList<PlanningSalarie>? plannings = null,
        IReadOnlyList<CategorieSalarie>? affectations = null,
        IReadOnlyList<ToleranceRetard>? tolerances = null,
        IReadOnlySet<DateTime>? feries = null,
        DateTime? from = null,
        DateTime? to = null)
        => RetardReportRules.Calculer(
            new EmployeeSources(cartes, plannings ?? Array.Empty<PlanningSalarie>(), corrections),
            new RetardTolerances((affectations ?? Array.Empty<CategorieSalarie>())
                .GroupBy(a => a.MatriculeSage)
                .ToDictionary(g => g.Key, g => g.First().IdCateg), tolerances ?? Array.Empty<ToleranceRetard>()),
            feries ?? new HashSet<DateTime>(),
            new ReportPeriod(from ?? Mardi, to ?? Mardi, Today));

    [Fact]
    public void Calculer_EntreeApresTolerance_RetourneLeRetardEnMinutes()
    {
        // Act
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        // Assert
        var row = result.Should().ContainSingle().Which;
        row.Matricule.Should().Be("001");
        row.Nom.Should().Be("Dupont");
        row.Prenom.Should().Be("Jean");
        row.Departement.Should().Be("INFO");
        row.Date.Should().Be(Mardi);
        row.HeureEntree.Should().Be(TimeSpan.Parse("08:45"));
        row.HeurePrevue.Should().Be(TimeSpan.Parse("08:00"));
        row.ToleranceMinutes.Should().Be(10);
        row.MinutesRetard.Should().Be(35);
        row.RetardSaisi.Should().BeFalse();
    }

    [Fact]
    public void Calculer_EntreeDansLaTolerance_AucuneLigne()
    {
        // Act
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:05") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_SansPlanning_AucuneLigne()
    {
        // Act
        // Le desktop retombait sur 00:00 : la référence est désormais le planning, comme le dashboard.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") });

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "17:00")]
    [InlineData("00:00", "00:00")]
    public void Calculer_HeuresDuPlanningNonExploitables_AucuneLigne(string? ha, string? hd)
    {
        // Act
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") },
            new[] { Plan(1, 1, Mardi, ha, hd) });

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_JourOff_AucunRetard()
    {
        // Act
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:45") },
            new[] { Plan(1, 1, Mardi, offP: true) });

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_WeekEndEtFerie_Exclus()
    {
        // Arrange
        var ferie = new DateTime(2025, 11, 5);

        // Act
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

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_AbsenceMatin_ArriveeApresMidi_NEstPasUnRetard()
    {
        // Act
        // Pas de retard « de midi » : la journée est couverte par une absence du matin.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "14:00", absenceAm: true) },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_DeuxPairesMemeJour_SeuleLaPremiereEntreeCompte()
    {
        // Act
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

        // Assert
        var row = result.Should().ContainSingle().Which;
        row.HeureEntree.Should().Be(TimeSpan.Parse("08:30"));
        row.MinutesRetard.Should().Be(20);
    }

    [Fact]
    public void Calculer_MonoUneSeulePaire_MemeResultat()
    {
        // Act
        // Mono-pointage : une paire par jour, entrée 08:30 -> 20 minutes.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:30") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        // Assert
        var row = result.Should().ContainSingle().Which;
        row.MinutesRetard.Should().Be(20);
    }

    [Fact]
    public void Calculer_RetardSaisi_PrioritaireSurLeCalcul()
    {
        // Act
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:00", retard: "00:42:00") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        // Assert
        var row = result.Should().ContainSingle().Which;
        row.MinutesRetard.Should().Be(42);
        row.RetardSaisi.Should().BeTrue();
    }

    [Fact]
    public void Calculer_RetardSaisiAZero_AucuneLigne()
    {
        // Act
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "09:30", retard: "00:00:00") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_CorrectionSansEntreeNiRetard_AucuneLigne()
    {
        // Act
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: null) },
            new[] { Plan(1, 1, Mardi) });

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_JourFutur_EstExclu()
    {
        // Act
        // Today = 20/11 : le 20 et le 21 ne sont jamais contrôlés.
        var result = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Today, entree: "09:30"), Corr(2, "001", Today.AddDays(1), entree: "09:30") },
            new[] { Plan(1, 1, Today), Plan(2, 1, Today.AddDays(1)) },
            from: Today,
            to: Today.AddDays(5));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_ToleranceParCategorie_AppliqueLaToleranceDeLaCategorie()
    {
        // Arrange
        // Sans tolérance : 08:05 -> 5 minutes. Avec 10 minutes : aucune ligne.
        var sansTolerance = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:05") },
            new[] { Plan(1, 1, Mardi) });

        // Act
        var avecTolerance = Calculer(
            new[] { Carte(1, "001") },
            new[] { Corr(1, "001", Mardi, entree: "08:05") },
            new[] { Plan(1, 1, Mardi) },
            new[] { Affectation(1, 10) },
            new[] { Tolerance(10, 10) });

        // Assert
        sansTolerance.Should().ContainSingle().Which.MinutesRetard.Should().Be(5);
        avecTolerance.Should().BeEmpty();
    }

    [Fact]
    public void Calculer_DeuxSalaries_TriParDatePuisMatricule()
    {
        // Arrange
        var j1 = new DateTime(2025, 11, 4);
        var j2 = new DateTime(2025, 11, 5);

        // Act
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

        // Assert
        result.Count.Should().Be(3);
        result[0].Date.Should().Be(j1);
        result[0].Matricule.Should().Be("002");
        result[1].Date.Should().Be(j2);
        result[1].Matricule.Should().Be("001");
        result[2].Date.Should().Be(j2);
        result[2].Matricule.Should().Be("002");
    }

    [Fact]
    public void MinutesRetard_EntreeEnAvance_RetourneZero()
    {
        // Act & Assert
        RetardReportRules.MinutesRetard(TimeSpan.Parse("07:55"), TimeSpan.Parse("08:00"), 10).Should().Be(0);
        RetardReportRules.MinutesRetard(TimeSpan.Parse("08:05"), TimeSpan.Parse("08:00"), 10).Should().Be(0);
        RetardReportRules.MinutesRetard(TimeSpan.Parse("08:45"), TimeSpan.Parse("08:00"), 10).Should().Be(35);
    }

    [Fact]
    public void HeurePrevue_PlanningOffOuSentinelle_EstNulle()
    {
        // Act & Assert
        RetardReportRules.HeurePrevue(null).Should().BeNull();
        RetardReportRules.HeurePrevue(Plan(1, 1, Mardi, offP: true)).Should().BeNull();
        RetardReportRules.HeurePrevue(Plan(1, 1, Mardi, ha: null)).Should().BeNull();
        RetardReportRules.HeurePrevue(new PlanningSalarie
        {
            Id = 1,
            DateP = Mardi,
            Ha = new DateTime(1800, 1, 1, 8, 0, 0)
        }).Should().BeNull();
        RetardReportRules.HeurePrevue(Plan(1, 1, Mardi)).Should().Be(TimeSpan.Parse("08:00"));
    }
}
