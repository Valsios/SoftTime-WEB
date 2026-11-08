using System.Globalization;
using System.Text;

using SoftTime.Application.ImportTemplates;
using SoftTime.Infrastructure.Excel;

namespace SoftTime.Tests;

/// <summary>
/// Catalogue des modèles d'import : cohérence des matrices/exemples et aller-retour des fichiers
/// générés (.xlsx relu par <see cref="ExcelWorkbook"/>, .csv découpé). L'import réel de chaque
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

        cles.Should().Equal(new[]
            {
                "shifts", "punches", "corrections",
                "planning-matricule", "planning-categorie",
                "planning-generate-category", "planning-generate-matricule",
                "affectations", "correspondences"
            });
        cles.Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(cles.Count);
    }

    [Fact]
    public void ChaqueModele_EstCoherent()
    {
        foreach (var modele in Modeles())
        {
            new[] { ImportTemplateCatalog.FormatXlsx, ImportTemplateCatalog.FormatCsv }.Should().Contain(modele.Format);
            modele.FileName.Should().EndWithEquivalentOf("." + modele.Format);
            modele.Columns.Should().NotBeEmpty();
            modele.ExampleRows.Should().NotBeEmpty();
            modele.Notes.Should().NotBeEmpty();
            modele.ExampleRows.Should().AllSatisfy(ligne => ligne.Count.Should().Be(modele.Columns.Count));
            modele.Headers.ToList().Should().Equal(modele.Columns.Select(c => c.Header).ToList());
        }
    }

    [Fact]
    public void ModelesPlanning_OntUneColonneParJour()
    {
        var dates = Enumerable.Range(0, 7)
            .Select(i => LundiReference.AddDays(i).ToString("yyyy-MM-dd"))
            .ToList();

        var parMatricule = Modeles().Should().ContainSingle(m => m.Key == "planning-matricule").Which;
        var parCategorie = Modeles().Should().ContainSingle(m => m.Key == "planning-categorie").Which;

        parMatricule.Columns[0].Header.Should().Be("MATRICULE");
        parCategorie.Columns[0].Header.Should().Be("CATEGORIE");
        foreach (var modele in new[] { parMatricule, parCategorie })
        {
            modele.Columns.Count.Should().Be(8);
            modele.Columns.Skip(1).Select(c => c.Header).ToList().Should().Equal(dates);
            modele.ExampleRows.Should().AllSatisfy(ligne => ligne.Count.Should().Be(8));
        }
    }

    [Fact]
    public void FichiersXlsx_Relus_RedonnentLesEnTetesEtLesExemples()
    {
        foreach (var modele in Modeles().Where(m => m.Format == ImportTemplateCatalog.FormatXlsx))
        {
            using var flux = new MemoryStream(ImportTemplateFile.Build(modele, new ExcelWorkbook()));
            var lignes = new ExcelWorkbook().ReadFirstSheet(flux);

            lignes.Count.Should().Be(modele.ExampleRows.Count);
            lignes[0].Keys.ToList().Should().Equal(modele.Headers.ToList());

            for (var i = 0; i < modele.ExampleRows.Count; i++)
            {
                for (var c = 0; c < modele.Columns.Count; c++)
                {
                    var entete = modele.Headers[c];
                    Normaliser(lignes[i][entete]).Should().Be(Normaliser(modele.ExampleRows[i][c]));
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
                .GetString(ImportTemplateFile.Build(modele, new ExcelWorkbook()))
                .TrimStart('\uFEFF');
            var lignes = texte.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            lignes[0].Split(';').Should().Equal(modele.Headers.ToArray());
            (lignes.Length - 1).Should().Be(modele.ExampleRows.Count);

            for (var i = 0; i < modele.ExampleRows.Count; i++)
            {
                var cellules = lignes[i + 1].Split(';');
                cellules.Length.Should().Be(modele.ExampleRows[i].Count);
                cellules.Select(Normaliser).ToArray().Should().Equal(modele.ExampleRows[i].Select(Normaliser).ToArray());
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
