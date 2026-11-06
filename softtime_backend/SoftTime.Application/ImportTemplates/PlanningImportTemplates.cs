
namespace SoftTime.Application.ImportTemplates;

public static class PlanningImportTemplates
{

    internal static readonly string[] JoursNoms = { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };

    internal static ImportTemplateDefinition PlanningMatricule(IReadOnlyList<DateTime> jours)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new(
                "MATRICULE",
                true,
                "texte",
                ImportTemplateExamples.MatriculeExemple,
                "La première colonne détermine le mode : MATRICULE (par salarié) ou CATEGORIE (par catégorie).")
        };
        colonnes.AddRange(jours.Select(j => new ImportTemplateColumn(
            j.ToString("yyyy-MM-dd"), false, "n° de shift / OFF", "1", "Vide = jour non modifié.")));

        var lignes = new List<IReadOnlyList<string>>
        {
            new List<string> { ImportTemplateExamples.MatriculeExemple, "1", "1", "1", "1", "1", "OFF", "OFF" },
            new List<string> { ImportTemplateExamples.MatriculeExemple2, "2", "2", "OFF", "2", "2", "OFF", "OFF" }
        };

        return new ImportTemplateDefinition(
            "planning-matricule",
            "Planning salariés — par matricule",
            ImportTemplateCatalog.FormatXlsx,
            "modele-planning-matricule.xlsx",
            "Planning",
            "Matrice : une ligne par salarié, une colonne par jour, cellule = numéro de shift, OFF (repos) ou vide.",
            colonnes,
            lignes,
            new[]
            {
                "Les en-têtes de colonnes doivent être des dates (aaaa-mm-jj ou jj/mm/aaaa).",
                "Le numéro de shift doit exister dans le catalogue Shifts.",
                "Le planning existant du jour est mis à jour."
            });
    }

    internal static ImportTemplateDefinition PlanningCategorie(IReadOnlyList<DateTime> jours)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new(
                "CATEGORIE",
                true,
                "texte",
                ImportTemplateExamples.CategorieExemple,
                "Intitulé exact d'une catégorie ; la ligne s'applique à tous les salariés affectés.")
        };
        colonnes.AddRange(jours.Select(j => new ImportTemplateColumn(
            j.ToString("yyyy-MM-dd"), false, "n° de shift / OFF", "1", "Vide = jour non modifié.")));

        var lignes = new List<IReadOnlyList<string>>
        {
            new List<string> { ImportTemplateExamples.CategorieExemple, "1", "1", "1", "1", "1", "OFF", "OFF" }
        };

        return new ImportTemplateDefinition(
            "planning-categorie",
            "Planning salariés — par catégorie",
            ImportTemplateCatalog.FormatXlsx,
            "modele-planning-categorie.xlsx",
            "Planning",
            "Matrice : une ligne par catégorie, une colonne par jour, cellule = numéro de shift, OFF ou vide.",
            colonnes,
            lignes,
            new[]
            {
                "L'intitulé de la catégorie doit exister (insensible à la casse, accents compris).",
                "Le numéro de shift doit exister dans le catalogue Shifts.",
                "Le planning existant du jour est mis à jour."
            });
    }

    // ------------------------------------------------------------------ génération de planning (CSV)

    internal static ImportTemplateDefinition PlanningGenerateCategorie(DateTime lundi)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new("Categorie", true, "texte", ImportTemplateExamples.CategorieExemple, "Intitulé exact de la catégorie."),
            new("Lundi", true, "date", lundi.ToString("yyyy-MM-dd"), "Doit impérativement être un lundi.")
        };
        var ligne = new List<string> { ImportTemplateExamples.CategorieExemple, lundi.ToString("yyyy-MM-dd") };

        for (var i = 0; i < 7; i++)
        {
            var off = i >= 5;
            colonnes.Add(new ImportTemplateColumn(
                $"{JoursNoms[i]}_debut", false, "HH:mm / OFF", off ? "OFF" : "08:00",
                i == 0 ? "OFF pour un jour non travaillé." : null));
            colonnes.Add(new ImportTemplateColumn($"{JoursNoms[i]}_fin", false, "HH:mm", off ? string.Empty : "17:00"));
            ligne.Add(off ? "OFF" : "08:00");
            ligne.Add(off ? string.Empty : "17:00");
        }

        return new ImportTemplateDefinition(
            "planning-generate-category",
            "Génération planning — par catégorie (CSV)",
            ImportTemplateCatalog.FormatCsv,
            "modele-planning-generate-categorie.csv",
            "Planning",
            "CSV « catégorie ; lundi ; (début ; fin) × 7 » : génère le planning de tous les salariés affectés à la catégorie.",
            colonnes,
            new IReadOnlyList<string>[] { ligne },
            new[]
            {
                "La pause appliquée est celle de la catégorie.",
                "Lundi doit être un lundi (aaaa-mm-jj ou jj/mm/aaaa).",
                "OFF pour un jour non travaillé ; les heures sont au format HH:mm.",
                "Séparateur « ; » (la virgule est aussi acceptée)."
            });
    }

    internal static ImportTemplateDefinition PlanningGenerateMatricule(DateTime lundi)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new("Matricule", true, "texte", ImportTemplateExamples.MatriculeExemple),
            new("Lundi", true, "date", lundi.ToString("yyyy-MM-dd"), "Doit impérativement être un lundi.")
        };
        var ligne = new List<string> { ImportTemplateExamples.MatriculeExemple, lundi.ToString("yyyy-MM-dd") };

        for (var i = 0; i < 7; i++)
        {
            var off = i >= 5;
            colonnes.Add(new ImportTemplateColumn(
                $"{JoursNoms[i]}_debut", false, "HH:mm / OFF", off ? "OFF" : "08:00",
                i == 0 ? "OFF pour un jour non travaillé." : null));
            colonnes.Add(new ImportTemplateColumn($"{JoursNoms[i]}_pause", false, "HH:mm", off ? string.Empty : "00:30"));
            colonnes.Add(new ImportTemplateColumn($"{JoursNoms[i]}_fin", false, "HH:mm", off ? string.Empty : "17:00"));
            ligne.Add(off ? "OFF" : "08:00");
            ligne.Add(off ? string.Empty : "00:30");
            ligne.Add(off ? string.Empty : "17:00");
        }

        return new ImportTemplateDefinition(
            "planning-generate-matricule",
            "Génération planning — par matricule (CSV)",
            ImportTemplateCatalog.FormatCsv,
            "modele-planning-generate-matricule.csv",
            "Planning",
            "CSV « matricule ; lundi ; (début ; pause ; fin) × 7 » : génère le planning d'un ou plusieurs salariés.",
            colonnes,
            new IReadOnlyList<string>[] { ligne },
            new[]
            {
                "Une pause par jour (vide = 00:00).",
                "Lundi doit être un lundi (aaaa-mm-jj ou jj/mm/aaaa).",
                "OFF pour un jour non travaillé ; les heures sont au format HH:mm.",
                "Séparateur « ; » (la virgule est aussi acceptée)."
            });
    }
}
