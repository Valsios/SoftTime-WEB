using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SoftTime.Api.Controllers;
using SoftTime.Application.DTOs;
using SoftTime.Application.Serialization;
using SoftTime.Domain.Models;

namespace SoftTime.Tests;

/// <summary>
/// Caractérisation du CONTRAT de la clôture de paie assistée, figé avant toute évolution de l'écran :
/// noms des propriétés JSON servies au client, route, droit requis et paramètres de requête. Les
/// assertions vérifient la PRÉSENCE des noms actuels (jamais l'ensemble exact) : un ajout ADDITIF
/// reste permis, un renommage ou une suppression casse le test.
/// </summary>
public class ClosingContractCharacterizationTests
{
    /// <summary>Options de sérialisation de l'API (Program.cs : défauts web + Configure).</summary>
    private static JsonSerializerOptions OptionsApi()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SoftTimeJsonOptions.Configure(options);
        return options;
    }

    private static ClosingReportDto RapportComplet() => new(
        new DateTime(2025, 11, 1),
        new DateTime(2025, 11, 30),
        "INFO",
        "ACorriger",
        "À corriger",
        0.05m,
        76,
        "formule",
        10,
        4,
        new[] { new ClosingCountDto("Critique", 2) },
        new[] { new ClosingCountDto("PointageManquant", 1) },
        new PagedResult<ClosingAnomalyViewDto>(
            new[]
            {
                new ClosingAnomalyViewDto(
                    "PointageManquant", "Critique", 5, "002", "NOM", "Prénom", "RH",
                    new DateTime(2025, 11, 17), "détail", 8.5m, null)
            },
            1, 1, 50, 1),
        new[]
        {
            new ClosingDepartmentDto("INFO", 72, 5, 3,
                new[] { new ClosingCountDto("Critique", 1) },
                new[] { new ClosingCountDto("DoublonExact", 1) })
        },
        new ClosingComparisonDto(
            new DateTime(2025, 10, 1), new DateTime(2025, 10, 31), 70, 9, 6,
            new[] { new ClosingTypeDeltaDto("PointageManquant", 2, 1, -1) }),
        true,
        1);

    private static void ContientLesCles(JsonElement element, params string[] cles)
    {
        var presentes = element.EnumerateObject().Select(p => p.Name).ToList();
        foreach (var cle in cles)
            presentes.Should().Contain(cle);
    }

    [Fact]
    public void Report_Serialized_KeepsEveryCurrentPropertyName()
    {
        // Arrange
        var rapport = RapportComplet();

        // Act
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(rapport, OptionsApi()));
        var racine = document.RootElement;

        // Assert
        ContientLesCles(racine,
            "from", "to", "departement", "statut", "statutLibelle", "seuilBloquant", "score", "formule",
            "joursEmployeControles", "nombreAnomalies", "parGravite", "parType", "anomalies",
            "parDepartement", "comparaison", "nomsAffiches", "salariesSansPlanningExclus");
        ContientLesCles(racine.GetProperty("anomalies"), "items", "total", "page", "pageSize", "totalPages");
        ContientLesCles(racine.GetProperty("anomalies").GetProperty("items")[0],
            "type", "gravite", "poids", "matricule", "nom", "prenom", "departement", "date", "detail",
            "heures", "pointageExiste");
        ContientLesCles(racine.GetProperty("parGravite")[0], "cle", "nombre");
        ContientLesCles(racine.GetProperty("parDepartement")[0],
            "departement", "score", "joursEmployeControles", "nombreAnomalies", "parGravite", "parType");
        ContientLesCles(racine.GetProperty("comparaison"),
            "from", "to", "scorePrecedent", "joursEmployeControlesPrecedent", "ecartScore", "parType");
        ContientLesCles(racine.GetProperty("comparaison").GetProperty("parType")[0],
            "type", "precedent", "actuel", "ecart");
    }

    [Fact]
    public void Report_WithoutNewFields_KeepsLegacyConstructionAndDefaults()
    {
        // Arrange : construction positionnelle d'origine (17 arguments) : les ajouts du lot B sont facultatifs.
        var rapport = RapportComplet();

        // Act
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(rapport, OptionsApi()));

        // Assert : champs additifs présents, à leur valeur neutre.
        document.RootElement.GetProperty("parSalarie").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("salariesConcernes").GetInt32().Should().Be(0);
    }

    [Fact]
    public void Checklist_Serialized_KeepsEveryCurrentPropertyName()
    {
        // Arrange
        var checklist = new ClosingChecklistDto(
            new DateTime(2025, 11, 1), new DateTime(2025, 11, 30), null,
            new[] { new ClosingChecklistLineDto("Pointages", "Pointages importés", "Fait", "détail", 12) });

        // Act
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(checklist, OptionsApi()));
        var racine = document.RootElement;

        // Assert
        ContientLesCles(racine, "from", "to", "departement", "lignes");
        ContientLesCles(racine.GetProperty("lignes")[0], "code", "libelle", "etat", "detail", "nombre");
    }

    [Fact]
    public void Controller_RouteRightAndVerbs_AreFrozen()
    {
        // Arrange
        var type = typeof(ClosingController);

        // Act
        var route = type.GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>().Single();
        var analysis = type.GetMethod(nameof(ClosingController.AnalysisAsync))!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Cast<HttpGetAttribute>().Single();
        var checklist = type.GetMethod(nameof(ClosingController.ChecklistAsync))!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Cast<HttpGetAttribute>().Single();

        // Assert
        route.Template.Should().Be("api/v1/closing");
        analysis.Template.Should().Be("analysis");
        checklist.Template.Should().Be("checklist");
        ClosingController.Permission.Should().Be("REPORTS:CLOSING");
        type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Should().NotBeEmpty();
    }

    [Fact]
    public void Query_CurrentParameters_AreFrozen()
    {
        // Arrange
        var proprietes = typeof(ClosingAnalysisQuery).GetProperties().Select(p => p.Name).ToList();

        // Assert
        foreach (var attendu in new[]
                 {
                     "From", "To", "Departement", "IncludeComparison", "IncludeInformation",
                     "Page", "PageSize", "ReuseCache", "Gravites", "Tri"
                 })
            proprietes.Should().Contain(attendu);
    }

    [Fact]
    public void AnomalyTypes_CurrentValues_AreFrozen()
    {
        // Arrange
        var noms = Enum.GetNames<ClosingAnomalyType>();

        // Assert : les 7 types actuels existent toujours sous le même nom (un ajout reste permis).
        foreach (var attendu in new[]
                 {
                     "PointageIncomplet", "PointageManquant", "PlanningSansPointage",
                     "PointageSansPlanning", "DoublonExact", "QuasiDoublon", "FerieNonApplique"
                 })
            noms.Should().Contain(attendu);
        Enum.GetNames<ClosingSeverity>().Should().BeEquivalentTo("Information", "Mineure", "Importante", "Critique");
    }
}
