using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Closing;

namespace SoftTime.Tests;

/// <summary>
/// Salariés les plus touchés (lot B1) : regroupement en mémoire des anomalies déjà calculées, rang par poids
/// cumulé, plafond, comptages par gravité et types présents. Aucune lecture : la règle est pure.
/// </summary>
public class ClosingEmployeeRankingTests
{
    private static readonly DateTime Lundi = new(2025, 11, 17);

    private static ClosingAnomaly A(string matricule, ClosingAnomalyType type, ClosingSeverity gravite, int jour = 0) =>
        new(type, gravite, matricule, Lundi.AddDays(jour), "détail");

    private static readonly Dictionary<string, string?> Departements = new(StringComparer.Ordinal)
    {
        ["1"] = "ATELIER", ["2"] = "RH", ["3"] = null
    };

    [Fact]
    public void Classer_RanksByCumulatedWeightThenCountThenMatricule()
    {
        // Arrange : 1 = un critique (5) ; 2 = trois mineures (3) ; 3 = un critique (5) + une information (0).
        var anomalies = new[]
        {
            A("2", ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, 0),
            A("1", ClosingAnomalyType.PointageManquant, ClosingSeverity.Critique, 1),
            A("2", ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, 2),
            A("3", ClosingAnomalyType.PointageIncomplet, ClosingSeverity.Critique, 3),
            A("2", ClosingAnomalyType.DoublonExact, ClosingSeverity.Mineure, 4),
            A("3", ClosingAnomalyType.FerieNonApplique, ClosingSeverity.Information, 4),
        };

        // Act
        var (top, concernes) = ClosingEmployeeRanking.Classer(anomalies, Departements, ClosingRulesOptions.Default);

        // Assert : 3 (poids 5, 2 anomalies) avant 1 (poids 5, 1 anomalie) avant 2 (poids 3).
        concernes.Should().Be(3);
        top.Select(s => s.Matricule).Should().Equal("3", "1", "2");
        top[0].Poids.Should().Be(5);
        top[0].NombreAnomalies.Should().Be(2);
        top[0].Departement.Should().BeNull();
        top[1].Departement.Should().Be("ATELIER");
    }

    [Fact]
    public void Classer_CountsPerSeverityAndOnlyPresentTypes()
    {
        // Arrange
        var anomalies = new[]
        {
            A("2", ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure),
            A("2", ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, 1),
            A("2", ClosingAnomalyType.PointageManquant, ClosingSeverity.Critique, 2),
        };

        // Act
        var salarie = ClosingEmployeeRanking.Classer(anomalies, Departements, ClosingRulesOptions.Default).Top.Single();

        // Assert
        salarie.ParGravite.Single(g => g.Cle == "Critique").Nombre.Should().Be(1);
        salarie.ParGravite.Single(g => g.Cle == "Mineure").Nombre.Should().Be(2);
        salarie.ParGravite.Should().HaveCount(Enum.GetValues<ClosingSeverity>().Length);
        salarie.ParType.Should().Equal(
            new ClosingCountDto("PointageManquant", 1),
            new ClosingCountDto("QuasiDoublon", 2));
    }

    [Fact]
    public void Classer_ManyEmployees_IsCappedButCountsEveryone()
    {
        // Arrange : 30 salariés, un critique chacun.
        var anomalies = Enumerable.Range(1, 30)
            .Select(i => A(i.ToString("000"), ClosingAnomalyType.PointageManquant, ClosingSeverity.Critique))
            .ToList();

        // Act
        var (top, concernes) = ClosingEmployeeRanking.Classer(anomalies, Departements, ClosingRulesOptions.Default);

        // Assert
        concernes.Should().Be(30);
        top.Should().HaveCount(ClosingEmployeeRanking.MaxSalaries);
        top.Select(s => s.Matricule).Should().BeInAscendingOrder(StringComparer.Ordinal);   // égalité : matricule
    }

    [Fact]
    public void Classer_NoAnomaly_IsEmpty()
    {
        var (top, concernes) = ClosingEmployeeRanking.Classer(Array.Empty<ClosingAnomaly>(), Departements, ClosingRulesOptions.Default);
        top.Should().BeEmpty();
        concernes.Should().Be(0);
    }

    [Fact]
    public void List_MatriculeFilter_UsesTheCanonicalKeyAndKeepsOtherFilters()
    {
        // Arrange
        var anomalies = new[]
        {
            A("777", ClosingAnomalyType.PointageManquant, ClosingSeverity.Critique),
            A("777", ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, 1),
            A("888", ClosingAnomalyType.PointageManquant, ClosingSeverity.Critique),
        };

        // Act : « 00777 » et « 777 » désignent le même salarié ; le filtre de type reste combiné.
        var tout = ClosingComputation.AnomaliesAffichees(anomalies,
            new ClosingAnalysisRequest(Lundi, Lundi.AddDays(4), Matricule: "00777"));
        var combine = ClosingComputation.AnomaliesAffichees(anomalies,
            new ClosingAnalysisRequest(Lundi, Lundi.AddDays(4), Matricule: "777",
                Types: new[] { ClosingAnomalyType.QuasiDoublon }));

        // Assert
        tout.Should().HaveCount(2).And.OnlyContain(a => a.Matricule == "777");
        combine.Should().ContainSingle().Which.Type.Should().Be(ClosingAnomalyType.QuasiDoublon);
    }
}
