using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Closing;

namespace SoftTime.Tests;

/// <summary>
/// Filtres de la LISTE d'anomalies (types, gravités, matricule) : lecture des paramètres et application à la liste.
/// Les comptages, le score et le statut ne dépendent jamais de ces filtres (vérifié par ClosingServiceTests et
/// ClosingHttpTests) ; ici, seules la lecture et la sélection sont contrôlées.
/// </summary>
public class ClosingListFiltersTests
{
    private static readonly DateTime Lundi = new(2025, 11, 17);

    private static ClosingAnomaly Anomalie(
        ClosingAnomalyType type, ClosingSeverity gravite = ClosingSeverity.Critique, string matricule = "001") =>
        new(type, gravite, matricule, Lundi, "détail");

    private static readonly IReadOnlyList<ClosingAnomaly> Liste = new[]
    {
        Anomalie(ClosingAnomalyType.PointageManquant),
        Anomalie(ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure),
        Anomalie(ClosingAnomalyType.DoublonExact, ClosingSeverity.Information),
        Anomalie(ClosingAnomalyType.PointageIncomplet, ClosingSeverity.Critique, "002")
    };

    private static ClosingAnalysisRequest Requete(
        IReadOnlyList<ClosingAnomalyType>? types = null,
        IReadOnlyList<ClosingSeverity>? gravites = null,
        bool information = false) =>
        new(Lundi, Lundi.AddDays(4), Gravites: gravites, IncludeInformation: information, Types: types);

    // ---------- Lecture du paramètre types ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TypeFilter_EmptyValue_MeansNoFilter(string? valeur)
    {
        ClosingTypeFilter.TryParse(valeur, out var types).Should().BeTrue();
        types.Should().BeNull();
    }

    [Fact]
    public void TypeFilter_ListWithCaseAndDuplicates_IsReadOnce()
    {
        // Act
        var ok = ClosingTypeFilter.TryParse(" quasidoublon , DoublonExact,QUASIDOUBLON ", out var types);

        // Assert
        ok.Should().BeTrue();
        types.Should().Equal(ClosingAnomalyType.QuasiDoublon, ClosingAnomalyType.DoublonExact);
    }

    [Theory]
    [InlineData("Inconnu")]
    [InlineData("QuasiDoublon,Pas-un-type")]
    [InlineData("3")]
    public void TypeFilter_UnknownValue_IsRefused(string valeur)
    {
        ClosingTypeFilter.TryParse(valeur, out var types).Should().BeFalse();
        types.Should().BeNull();
        var act = () => ClosingTypeFilter.Parse(valeur);
        act.Should().Throw<DomainException>().WithMessage(ClosingTypeFilter.Message);
    }

    // ---------- Application à la liste ----------

    [Fact]
    public void List_TypeFilter_KeepsOnlyThoseTypesWithoutRevealingHiddenInformation()
    {
        // Act : « Information » masquée par défaut, même si son type est demandé.
        var affichees = ClosingComputation.AnomaliesAffichees(Liste,
            Requete(types: new[] { ClosingAnomalyType.QuasiDoublon, ClosingAnomalyType.DoublonExact }));

        // Assert
        affichees.Should().ContainSingle().Which.Type.Should().Be(ClosingAnomalyType.QuasiDoublon);
    }

    [Fact]
    public void List_TypeFilterWithInformationIncluded_KeepsInformationRows()
    {
        // Act
        var affichees = ClosingComputation.AnomaliesAffichees(Liste,
            Requete(types: new[] { ClosingAnomalyType.DoublonExact }, information: true));

        // Assert
        affichees.Should().ContainSingle().Which.Severity.Should().Be(ClosingSeverity.Information);
    }

    [Fact]
    public void List_TypeAndGravityFilters_Combine()
    {
        // Act : gravité Critique + type PointageIncomplet → la seule ligne 002.
        var affichees = ClosingComputation.AnomaliesAffichees(Liste, Requete(
            types: new[] { ClosingAnomalyType.PointageIncomplet },
            gravites: new[] { ClosingSeverity.Critique }));

        // Assert
        affichees.Should().ContainSingle().Which.Matricule.Should().Be("002");
    }

    [Fact]
    public void List_NoTypeFilter_IsUnchangedBehaviour()
    {
        // Act
        var sans = ClosingComputation.AnomaliesAffichees(Liste, Requete());
        var vide = ClosingComputation.AnomaliesAffichees(Liste, Requete(types: Array.Empty<ClosingAnomalyType>()));

        // Assert : trois lignes (l'Information est masquée), identiques avec un filtre vide.
        sans.Should().HaveCount(3);
        vide.Should().Equal(sans);
    }
}
