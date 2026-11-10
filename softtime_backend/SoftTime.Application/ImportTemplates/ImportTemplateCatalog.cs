
namespace SoftTime.Application.ImportTemplates;

/// <summary>
/// Catalogue des modèles d'import (Excel et CSV), source unique de l'aide affichée à l'utilisateur
/// et des fichiers téléchargeables. Les en-têtes reproduisent ceux lus par les parseurs existants
/// (<c>TimekeepingService</c>, <c>PlanningService</c>, <c>CatalogService</c>) : un modèle importé
/// tel quel est accepté par l'endpoint correspondant.
///
/// Les modèles « matrice » (planning) sont datés sur la semaine du lundi de référence (par défaut
/// le lundi courant) ; les autres sont indépendants des dates.
/// </summary>
public static class ImportTemplateCatalog
{
    public const string FormatXlsx = "xlsx";

    public const string FormatCsv = "csv";

    /// <summary>Modèles disponibles, ordonnés pour l'affichage.</summary>
    public static IReadOnlyList<ImportTemplateDefinition> All(DateTime? semaineReference = null)
    {
        var lundi = LundiDe(semaineReference ?? DateTime.Today);
        var jours = Enumerable.Range(0, 7).Select(i => lundi.AddDays(i)).ToList();

        return new[]
        {
            Shifts(),
            Punches(),
            Corrections(),
            PlanningImportTemplates.PlanningMatricule(jours),
            PlanningImportTemplates.PlanningCategorie(jours),
            PlanningImportTemplates.PlanningGenerateCategorie(lundi),
            PlanningImportTemplates.PlanningGenerateMatricule(lundi),
            Affectations(),
            Correspondances()
        };
    }

    /// <summary>Modèle par clé (insensible à la casse), ou <c>null</c> si la clé est inconnue.</summary>
    public static ImportTemplateDefinition? Find(string key, DateTime? semaineReference = null)
        => All(semaineReference).FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    public static ImportTemplateDto ToDto(ImportTemplateDefinition definition) => new(
        definition.Key,
        definition.Label,
        definition.Format,
        definition.FileName,
        definition.Description,
        definition.Columns
            .Select(c => new ImportTemplateColumnDto(c.Header, c.Required, c.Format, c.Example, c.Note))
            .ToList(),
        definition.ExampleRows,
        definition.Notes);

    /// <summary>Lundi de la semaine contenant <paramref name="date"/>.</summary>
    public static DateTime LundiDe(DateTime date)
        => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    // ------------------------------------------------------------------ imports Excel

    internal static ImportTemplateDefinition Shifts() => new(
        Key: "shifts",
        Label: "Shifts — catalogue des horaires",
        Format: FormatXlsx,
        FileName: "modele-shifts.xlsx",
        SheetName: "Shifts",
        Description: "Un shift par ligne : numéro, intitulé, heure d'arrivée, pause et heure de départ. "
                   + "Le numéro est réutilisé par la matrice du planning.",
        Columns: new ImportTemplateColumn[]
        {
            new("NoSHIFT", true, "entier", "1", "Clé de mise à jour : le shift est créé s'il n'existe pas."),
            new("INTITULE", false, "texte", "Journée"),
            new("ENTREE", true, "HH:mm", "08:00"),
            new("PAUSE", false, "HH:mm", "00:30", "Vide = 00:00."),
            new("SORTIE", true, "HH:mm", "17:00", "Si l'heure est avant ENTREE, la sortie est le lendemain (nuit).")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { "1", "Journée", "08:00", "00:30", "17:00" },
            new[] { "2", "Nuit", "22:00", "00:30", "06:00" }
        },
        Notes: new[]
        {
            "Le numéro de shift est celui utilisé dans la matrice du planning (par matricule ou par catégorie).",
            "Les lignes d'exemple sont à remplacer par vos propres shifts."
        });

    internal static ImportTemplateDefinition Punches() => new(
        Key: "punches",
        Label: "Pointages",
        Format: FormatXlsx,
        FileName: "modele-pointages.xlsx",
        SheetName: "Pointages",
        Description: "Un pointage par ligne (matricule, date, heure). Entrée et sortie sont deux lignes.",
        Columns: new ImportTemplateColumn[]
        {
            new("Matricule", true, "texte", ImportTemplateExamples.MatriculeExemple, "Matricule SAGE du salarié."),
            new("Date", true, "date", "2026-11-02", "aaaa-mm-jj ou jj/mm/aaaa."),
            new("Heure", true, "HH:mm", "08:00")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { ImportTemplateExamples.MatriculeExemple, "2026-11-02", "08:00" },
            new[] { ImportTemplateExamples.MatriculeExemple, "2026-11-02", "17:00" }
        },
        Notes: new[]
        {
            "Une ligne = un pointage : une entrée et une sortie font deux lignes.",
            "Les doublons (même matricule, même date, même heure) sont ignorés.",
            "Le type est forcé à IO : lancez ensuite « Traiter les pointages » pour générer les heures corrigées."
        });

    internal static ImportTemplateDefinition Corrections() => new(
        Key: "corrections",
        Label: "Heures corrigées",
        Format: FormatXlsx,
        FileName: "modele-heures-corrigees.xlsx",
        SheetName: "Heures corrigées",
        Description: "Une paire entrée/sortie par ligne. La sortie peut tomber un autre jour (poste de nuit).",
        Columns: new ImportTemplateColumn[]
        {
            new("Matricule", true, "texte", ImportTemplateExamples.MatriculeExemple),
            new("DateEntree", true, "date", "2026-11-02"),
            new("HeureEntree", true, "HH:mm", "08:00"),
            new("DateSortie", true, "date", "2026-11-02", "Peut être le lendemain de DateEntree."),
            new("HeureSortie", true, "HH:mm", "17:00")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { ImportTemplateExamples.MatriculeExemple, "2026-11-02", "08:00", "2026-11-02", "17:00" },
            new[] { ImportTemplateExamples.MatriculeExemple2, "2026-11-02", "22:00", "2026-11-03", "06:00" }
        },
        Notes: new[]
        {
            "Les colonnes Absence, Férié, HPS/HPE ne sont pas importées (saisie manuelle sur l'écran).",
            "Les corrections importées sont protégées du traitement des pointages (ORIGINE = MANUEL)."
        });

    // ------------------------------------------------------------------ paramètres

    internal static ImportTemplateDefinition Affectations() => new(
        Key: "affectations",
        Label: "Affectations salariés ↔ catégories",
        Format: FormatXlsx,
        FileName: "modele-affectations.xlsx",
        SheetName: "Affectations",
        Description: "Une ligne par salarié : catégorie (intitulé exact) et matricule.",
        Columns: new ImportTemplateColumn[]
        {
            new("Categorie", true, "texte", ImportTemplateExamples.CategorieExemple, "Intitulé exact d'une catégorie existante."),
            new("Matricule", true, "texte", ImportTemplateExamples.MatriculeExemple, "Matricule d'une correspondance paie existante.")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { ImportTemplateExamples.CategorieExemple, ImportTemplateExamples.MatriculeExemple }
        },
        Notes: new[]
        {
            "La catégorie et le matricule doivent exister : les lignes inconnues sont ignorées.",
            "L'affectation existante du salarié est mise à jour."
        });

    internal static ImportTemplateDefinition Correspondances() => new(
        Key: "correspondences",
        Label: "Correspondances paie — badge ↔ matricule (CSV)",
        Format: FormatCsv,
        FileName: "modele-correspondances.csv",
        SheetName: "Correspondances",
        Description: "CSV « badge ; matricule » : associe un badge pointeuse au matricule SAGE du salarié.",
        Columns: new ImportTemplateColumn[]
        {
            new("Badge", true, "texte", "00123", "Numéro de badge pointeuse."),
            new("Matricule", true, "texte", ImportTemplateExamples.MatriculeExemple, "Matricule SAGE.")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { "00123", ImportTemplateExamples.MatriculeExemple },
            new[] { "00124", ImportTemplateExamples.MatriculeExemple2 }
        },
        Notes: new[]
        {
            "Séparateur « ; » ou « , ».",
            "La ligne d'en-tête est reconnue et ignorée.",
            "La correspondance est créée si le matricule n'existe pas encore ; sinon seul le badge est mis à jour."
        });
}
