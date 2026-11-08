using System.Text.Json;

using ClosedXML.Excel;

using SoftTime.Application.DTOs;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Tests;

/// <summary>
/// Classeur multi-feuilles utilisé par l'export du tableau de bord : chaque feuille garde ses
/// en-têtes et les valeurs numériques restent numériques (donc triables et additionnables).
/// </summary>
public class ExcelWorkbookTests
{
    [Fact]
    public void WriteWorkbook_EcritPlusieursFeuillesEtConserveLesTypes()
    {
        // Arrange
        var bytes = new ExcelWorkbook().WriteWorkbook(new[]
        {
            new ExcelSheetContent(
                "Synthèse",
                new[] { "Indicateur", "Valeur" },
                new IReadOnlyList<object?>[]
                {
                    new object?[] { "Période", "01/10/2026 → 31/10/2026" },
                    new object?[] { "Effectif actif", 12 },
                    new object?[] { "Taux", 12.5m },
                }),
            new ExcelSheetContent(
                "Périodes",
                new[] { "Période", "Absences" },
                new IReadOnlyList<object?>[]
                {
                    new object?[] { "Semaine 40", 3 },
                }),
        });
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        // Act
        var result = wb.Worksheets.Select(w => w.Name);

        // Assert
        result.Should().Equal(new[] { "Synthèse", "Périodes" });
        var synthese = wb.Worksheet("Synthèse");
        synthese.Cell(1, 1).GetString().Should().Be("Indicateur");
        synthese.Cell(2, 1).GetString().Should().Be("Période");
        synthese.Cell(3, 2).GetValue<int>().Should().Be(12);
        synthese.Cell(4, 2).GetValue<double>().Should().BeApproximately(12.5, 0.0005);
        var periodes = wb.Worksheet("Périodes");
        periodes.Cell(2, 2).GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void WriteWorkbook_NettoieEtDedupliqueLesNomsDeFeuilles()
    {
        // Arrange
        var bytes = new ExcelWorkbook().WriteWorkbook(new[]
        {
            new ExcelSheetContent("Répartition/Catégorie", new[] { "A" }, Array.Empty<IReadOnlyList<object?>>()),
            new ExcelSheetContent("Répartition/Catégorie", new[] { "B" }, Array.Empty<IReadOnlyList<object?>>()),
            new ExcelSheetContent(new string('x', 40), new[] { "C" }, Array.Empty<IReadOnlyList<object?>>()),
        });
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        // Act
        var noms = wb.Worksheets.Select(w => w.Name).ToList();

        // Assert
        noms.Count.Should().Be(3);
        noms.Should().Contain("Répartition-Catégorie");
        noms.Should().Contain("Répartition-Catégorie (2)");
        noms.Should().AllSatisfy(nom => (nom.Length <= 31).Should().BeTrue());
        noms.Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(noms.Count);
    }

    [Fact]
    public void WriteWorkbook_ConvertitLesValeursJsonEnCellulesTypees()
    {
        // Arrange
        using var doc = JsonDocument.Parse(
            """{"entier":7,"decimal":2.5,"texte":"ok","booleen":true,"vide":null}""");
        var json = doc.RootElement;
        var bytes = new ExcelWorkbook().WriteWorkbook(new[]
        {
            new ExcelSheetContent(
                "JSON",
                new[] { "Entier", "Décimal", "Texte", "Booléen", "Vide" },
                new IReadOnlyList<object?>[]
                {
                    new object?[]
                    {
                        json.GetProperty("entier"),
                        json.GetProperty("decimal"),
                        json.GetProperty("texte"),
                        json.GetProperty("booleen"),
                        json.GetProperty("vide"),
                    },
                }),
        });
        using var stream = new MemoryStream(bytes);
        using var sut = new XLWorkbook(stream);

        // Act
        var ws = sut.Worksheet("JSON");

        // Assert
        ws.Cell(2, 1).GetValue<int>().Should().Be(7);
        ws.Cell(2, 2).GetValue<double>().Should().BeApproximately(2.5, 0.0005);
        ws.Cell(2, 3).GetString().Should().Be("ok");
        ws.Cell(2, 4).GetValue<bool>().Should().BeTrue();
        ws.Cell(2, 5).IsEmpty().Should().BeTrue();
    }

    [Fact]
    public void WriteWorkbook_SansFeuille_ProduitUneFeuilleParDefaut()
    {
        var bytes = new ExcelWorkbook().WriteWorkbook(Array.Empty<ExcelSheetContent>());

        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        wb.Worksheets.Should().ContainSingle();
        wb.Worksheets.First().Name.Should().Be("Données");
    }
}
