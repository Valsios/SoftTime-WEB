#nullable enable

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

    private const string MatriculeExemple = "04285";
    private const string MatriculeExemple2 = "04301";
    private const string CategorieExemple = "Sédentaire";

    private static readonly string[] JoursNoms = { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };

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
            PlanningMatricule(jours),
            PlanningCategorie(jours),
            PlanningGenerateCategorie(lundi),
            PlanningGenerateMatricule(lundi),
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

    private static ImportTemplateDefinition Shifts() => new(
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

    private static ImportTemplateDefinition Punches() => new(
        Key: "punches",
        Label: "Pointages",
        Format: FormatXlsx,
        FileName: "modele-pointages.xlsx",
        SheetName: "Pointages",
        Description: "Un pointage par ligne (matricule, date, heure). Entrée et sortie sont deux lignes.",
        Columns: new ImportTemplateColumn[]
        {
            new("Matricule", true, "texte", MatriculeExemple, "Matricule SAGE du salarié."),
            new("Date", true, "date", "2026-11-02", "aaaa-mm-jj ou jj/mm/aaaa."),
            new("Heure", true, "HH:mm", "08:00")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { MatriculeExemple, "2026-11-02", "08:00" },
            new[] { MatriculeExemple, "2026-11-02", "17:00" }
        },
        Notes: new[]
        {
            "Une ligne = un pointage : une entrée et une sortie font deux lignes.",
            "Les doublons (même matricule, même date, même heure) sont ignorés.",
            "Le type est forcé à IO : lancez ensuite « Traiter les pointages » pour générer les heures corrigées."
        });

    private static ImportTemplateDefinition Corrections() => new(
        Key: "corrections",
        Label: "Heures corrigées",
        Format: FormatXlsx,
        FileName: "modele-heures-corrigees.xlsx",
        SheetName: "Heures corrigées",
        Description: "Une paire entrée/sortie par ligne. La sortie peut tomber un autre jour (poste de nuit).",
        Columns: new ImportTemplateColumn[]
        {
            new("Matricule", true, "texte", MatriculeExemple),
            new("DateEntree", true, "date", "2026-11-02"),
            new("HeureEntree", true, "HH:mm", "08:00"),
            new("DateSortie", true, "date", "2026-11-02", "Peut être le lendemain de DateEntree."),
            new("HeureSortie", true, "HH:mm", "17:00")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { MatriculeExemple, "2026-11-02", "08:00", "2026-11-02", "17:00" },
            new[] { MatriculeExemple2, "2026-11-02", "22:00", "2026-11-03", "06:00" }
        },
        Notes: new[]
        {
            "Les colonnes Absence, Férié, HPS/HPE ne sont pas importées (saisie manuelle sur l'écran).",
            "Les corrections importées sont protégées du traitement des pointages (ORIGINE = MANUEL)."
        });

    private static ImportTemplateDefinition PlanningMatricule(IReadOnlyList<DateTime> jours)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new(
                "MATRICULE",
                true,
                "texte",
                MatriculeExemple,
                "La première colonne détermine le mode : MATRICULE (par salarié) ou CATEGORIE (par catégorie).")
        };
        colonnes.AddRange(jours.Select(j => new ImportTemplateColumn(
            j.ToString("yyyy-MM-dd"), false, "n° de shift / OFF", "1", "Vide = jour non modifié.")));

        var lignes = new List<IReadOnlyList<string>>
        {
            new List<string> { MatriculeExemple, "1", "1", "1", "1", "1", "OFF", "OFF" },
            new List<string> { MatriculeExemple2, "2", "2", "OFF", "2", "2", "OFF", "OFF" }
        };

        return new ImportTemplateDefinition(
            "planning-matricule",
            "Planning salariés — par matricule",
            FormatXlsx,
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

    private static ImportTemplateDefinition PlanningCategorie(IReadOnlyList<DateTime> jours)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new(
                "CATEGORIE",
                true,
                "texte",
                CategorieExemple,
                "Intitulé exact d'une catégorie ; la ligne s'applique à tous les salariés affectés.")
        };
        colonnes.AddRange(jours.Select(j => new ImportTemplateColumn(
            j.ToString("yyyy-MM-dd"), false, "n° de shift / OFF", "1", "Vide = jour non modifié.")));

        var lignes = new List<IReadOnlyList<string>>
        {
            new List<string> { CategorieExemple, "1", "1", "1", "1", "1", "OFF", "OFF" }
        };

        return new ImportTemplateDefinition(
            "planning-categorie",
            "Planning salariés — par catégorie",
            FormatXlsx,
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

    private static ImportTemplateDefinition PlanningGenerateCategorie(DateTime lundi)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new("Categorie", true, "texte", CategorieExemple, "Intitulé exact de la catégorie."),
            new("Lundi", true, "date", lundi.ToString("yyyy-MM-dd"), "Doit impérativement être un lundi.")
        };
        var ligne = new List<string> { CategorieExemple, lundi.ToString("yyyy-MM-dd") };

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
            FormatCsv,
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

    private static ImportTemplateDefinition PlanningGenerateMatricule(DateTime lundi)
    {
        var colonnes = new List<ImportTemplateColumn>
        {
            new("Matricule", true, "texte", MatriculeExemple),
            new("Lundi", true, "date", lundi.ToString("yyyy-MM-dd"), "Doit impérativement être un lundi.")
        };
        var ligne = new List<string> { MatriculeExemple, lundi.ToString("yyyy-MM-dd") };

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
            FormatCsv,
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

    // ------------------------------------------------------------------ paramètres

    private static ImportTemplateDefinition Affectations() => new(
        Key: "affectations",
        Label: "Affectations salariés ↔ catégories",
        Format: FormatXlsx,
        FileName: "modele-affectations.xlsx",
        SheetName: "Affectations",
        Description: "Une ligne par salarié : catégorie (intitulé exact) et matricule.",
        Columns: new ImportTemplateColumn[]
        {
            new("Categorie", true, "texte", CategorieExemple, "Intitulé exact d'une catégorie existante."),
            new("Matricule", true, "texte", MatriculeExemple, "Matricule d'une correspondance paie existante.")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { CategorieExemple, MatriculeExemple }
        },
        Notes: new[]
        {
            "La catégorie et le matricule doivent exister : les lignes inconnues sont ignorées.",
            "L'affectation existante du salarié est mise à jour."
        });

    private static ImportTemplateDefinition Correspondances() => new(
        Key: "correspondences",
        Label: "Correspondances paie — badge ↔ matricule (CSV)",
        Format: FormatCsv,
        FileName: "modele-correspondances.csv",
        SheetName: "Correspondances",
        Description: "CSV « badge ; matricule » : associe un badge pointeuse au matricule SAGE du salarié.",
        Columns: new ImportTemplateColumn[]
        {
            new("Badge", true, "texte", "00123", "Numéro de badge pointeuse."),
            new("Matricule", true, "texte", MatriculeExemple, "Matricule SAGE.")
        },
        ExampleRows: new IReadOnlyList<string>[]
        {
            new[] { "00123", MatriculeExemple },
            new[] { "00124", MatriculeExemple2 }
        },
        Notes: new[]
        {
            "Séparateur « ; » ou « , ».",
            "La ligne d'en-tête est reconnue et ignorée.",
            "La correspondance est créée si le matricule n'existe pas encore ; sinon seul le badge est mis à jour."
        });
}
