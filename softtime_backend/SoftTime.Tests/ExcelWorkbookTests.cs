using System.Text.Json;
using ClosedXML.Excel;
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
        var bytes = ExcelHelper.WriteWorkbook(new[]
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

        Assert.Equal(new[] { "Synthèse", "Périodes" }, wb.Worksheets.Select(w => w.Name));

        var synthese = wb.Worksheet("Synthèse");
        Assert.Equal("Indicateur", synthese.Cell(1, 1).GetString());
        Assert.Equal("Période", synthese.Cell(2, 1).GetString());
        Assert.Equal(12, synthese.Cell(3, 2).GetValue<int>());
        Assert.Equal(12.5, synthese.Cell(4, 2).GetValue<double>(), 3);

        var periodes = wb.Worksheet("Périodes");
        Assert.Equal(3, periodes.Cell(2, 2).GetValue<int>());
    }

    [Fact]
    public void WriteWorkbook_NettoieEtDedupliqueLesNomsDeFeuilles()
    {
        var bytes = ExcelHelper.WriteWorkbook(new[]
        {
            new ExcelSheetContent("Répartition/Catégorie", new[] { "A" }, Array.Empty<IReadOnlyList<object?>>()),
            new ExcelSheetContent("Répartition/Catégorie", new[] { "B" }, Array.Empty<IReadOnlyList<object?>>()),
            new ExcelSheetContent(new string('x', 40), new[] { "C" }, Array.Empty<IReadOnlyList<object?>>()),
        });

        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);
        var noms = wb.Worksheets.Select(w => w.Name).ToList();

        Assert.Equal(3, noms.Count);
        Assert.Contains("Répartition-Catégorie", noms);
        Assert.Contains("Répartition-Catégorie (2)", noms);
        Assert.All(noms, nom => Assert.True(nom.Length <= 31));
        Assert.Equal(noms.Count, noms.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void WriteWorkbook_ConvertitLesValeursJsonEnCellulesTypees()
    {
        using var doc = JsonDocument.Parse(
            """{"entier":7,"decimal":2.5,"texte":"ok","booleen":true,"vide":null}""");
        var json = doc.RootElement;

        var bytes = ExcelHelper.WriteWorkbook(new[]
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
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet("JSON");

        Assert.Equal(7, ws.Cell(2, 1).GetValue<int>());
        Assert.Equal(2.5, ws.Cell(2, 2).GetValue<double>(), 3);
        Assert.Equal("ok", ws.Cell(2, 3).GetString());
        Assert.True(ws.Cell(2, 4).GetValue<bool>());
        Assert.True(ws.Cell(2, 5).IsEmpty());
    }

    [Fact]
    public void WriteWorkbook_SansFeuille_ProduitUneFeuilleParDefaut()
    {
        var bytes = ExcelHelper.WriteWorkbook(Array.Empty<ExcelSheetContent>());

        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        Assert.Single(wb.Worksheets);
        Assert.Equal("Données", wb.Worksheets.First().Name);
    }
}
