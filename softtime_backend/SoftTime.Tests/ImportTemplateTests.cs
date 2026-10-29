using System.Globalization;
using System.Text;
using SoftTime.Application.ImportTemplates;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Tests;

/// <summary>
/// Catalogue des modèles d'import : cohérence des matrices/exemples et aller-retour des fichiers
/// générés (.xlsx relu par <see cref="ExcelHelper"/>, .csv découpé). L'import réel de chaque
/// modèle est couvert par <see cref="ImportTemplateImportTests"/>.
/// </summary>
public class ImportTemplateTests
{
    private static readonly DateTime LundiReference = new(2026, 11, 2); // lundi

    private static IReadOnlyList<ImportTemplateDefinition> Modeles()
        => ImportTemplateCatalog.All(LundiReference);

    [Fact]
    public void Catalogue_ContientLesNeufModelesAttendus()
    {
        var cles = Modeles().Select(m => m.Key).ToList();

        Assert.Equal(
            new[]
            {
                "shifts", "punches", "corrections",
                "planning-matricule", "planning-categorie",
                "planning-generate-category", "planning-generate-matricule",
                "affectations", "correspondences"
            },
            cles);
        Assert.Equal(cles.Count, cles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ChaqueModele_EstCoherent()
    {
        foreach (var modele in Modeles())
        {
            Assert.Contains(modele.Format, new[] { ImportTemplateCatalog.FormatXlsx, ImportTemplateCatalog.FormatCsv });
            Assert.EndsWith("." + modele.Format, modele.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(modele.Columns);
            Assert.NotEmpty(modele.ExampleRows);
            Assert.NotEmpty(modele.Notes);
            Assert.All(modele.ExampleRows, ligne => Assert.Equal(modele.Columns.Count, ligne.Count));
            Assert.Equal(modele.Columns.Select(c => c.Header).ToList(), modele.Headers.ToList());
        }
    }

    [Fact]
    public void ModelesPlanning_OntUneColonneParJour()
    {
        var dates = Enumerable.Range(0, 7)
            .Select(i => LundiReference.AddDays(i).ToString("yyyy-MM-dd"))
            .ToList();

        var parMatricule = Assert.Single(Modeles(), m => m.Key == "planning-matricule");
        var parCategorie = Assert.Single(Modeles(), m => m.Key == "planning-categorie");

        Assert.Equal("MATRICULE", parMatricule.Columns[0].Header);
        Assert.Equal("CATEGORIE", parCategorie.Columns[0].Header);
        foreach (var modele in new[] { parMatricule, parCategorie })
        {
            Assert.Equal(8, modele.Columns.Count);
            Assert.Equal(dates, modele.Columns.Skip(1).Select(c => c.Header).ToList());
            Assert.All(modele.ExampleRows, ligne => Assert.Equal(8, ligne.Count));
        }
    }

    [Fact]
    public void FichiersXlsx_Relus_RedonnentLesEnTetesEtLesExemples()
    {
        foreach (var modele in Modeles().Where(m => m.Format == ImportTemplateCatalog.FormatXlsx))
        {
            using var flux = new MemoryStream(ImportTemplateFile.Build(modele));
            var lignes = ExcelHelper.ReadFirstSheet(flux);

            Assert.Equal(modele.ExampleRows.Count, lignes.Count);
            Assert.Equal(modele.Headers.ToList(), lignes[0].Keys.ToList());

            for (var i = 0; i < modele.ExampleRows.Count; i++)
            {
                for (var c = 0; c < modele.Columns.Count; c++)
                {
                    var entete = modele.Headers[c];
                    Assert.Equal(
                        Normaliser(modele.ExampleRows[i][c]),
                        Normaliser(lignes[i][entete]));
                }
            }
        }
    }

    [Fact]
    public void FichiersCsv_RedonnentLesEnTetesEtLesExemples()
    {
        foreach (var modele in Modeles().Where(m => m.Format == ImportTemplateCatalog.FormatCsv))
        {
            var texte = new UTF8Encoding(false)
                .GetString(ImportTemplateFile.Build(modele))
                .TrimStart('\uFEFF');
            var lignes = texte.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(modele.Headers.ToArray(), lignes[0].Split(';'));
            Assert.Equal(modele.ExampleRows.Count, lignes.Length - 1);

            for (var i = 0; i < modele.ExampleRows.Count; i++)
            {
                var cellules = lignes[i + 1].Split(';');
                Assert.Equal(modele.ExampleRows[i].Count, cellules.Length);
                Assert.Equal(
                    modele.ExampleRows[i].Select(Normaliser).ToArray(),
                    cellules.Select(Normaliser).ToArray());
            }
        }
    }

    /// <summary>Normalise les heures (Excel peut relire « 08:00 » en « 08:00:00 »).</summary>
    private static string Normaliser(string? valeur)
    {
        var texte = valeur?.Trim() ?? string.Empty;
        if (texte.Contains(':') && TimeSpan.TryParse(texte, CultureInfo.InvariantCulture, out var heure))
            return heure.ToString(@"hh\:mm");
        return texte;
    }
}
