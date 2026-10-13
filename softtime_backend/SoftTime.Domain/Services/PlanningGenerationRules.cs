namespace SoftTime.Domain.Services;

/// <summary>
/// Règles de génération de planning à partir d'un CSV, portées depuis l'application desktop
/// (Services/Traitements/ImportationPlanningService.cs) :
/// <list type="bullet">
///   <item>« par catégorie » : <c>catégorie ; lundi ; (début ; fin) × 7</c> ;</item>
///   <item>« par matricule » : <c>matricule ; lundi ; (début ; pause ; fin) × 7</c> ;</item>
///   <item><c>OFF</c> marque un jour non travaillé ;</item>
///   <item>une heure de fin inférieure à l'heure de début indique un poste de nuit (sortie le lendemain).</item>
/// </list>
/// Classe pure (aucune dépendance EF) afin d'être testable unitairement, sur le modèle de
/// <see cref="PunchPairingRules"/>.
/// </summary>
public static class PlanningGenerationRules
{
    /// <summary>Nombre de jours décrits par une ligne de planning.</summary>
    public const int JoursParSemaine = 7;

    /// <summary>Jeton indiquant un jour non travaillé.</summary>
    public const string JetonOff = "OFF";

    /// <summary>Indique si la cellule décrit un jour non travaillé (<c>OFF</c>, insensible à la casse).</summary>
    public static bool EstOff(string? valeur)
        => string.Equals(valeur?.Trim(), JetonOff, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Un jour de génération doit être un lundi : la semaine décrite commence toujours le lundi,
    /// comme dans le desktop (<c>parsedDate.DayOfWeek == DayOfWeek.Monday</c>).
    /// </summary>
    public static bool TryLundi(string? valeur, out DateTime lundi)
    {
        lundi = default;
        if (!DateTime.TryParse(valeur?.Trim(), out var date) || date.DayOfWeek != DayOfWeek.Monday)
            return false;
        lundi = date.Date;
        return true;
    }

    /// <summary>Heure au format « HH:mm » ou « HH:mm:ss » (comme les cellules du fichier desktop).</summary>
    public static bool TryHeure(string? valeur, out TimeSpan heure)
        => TimeSpan.TryParse(valeur?.Trim(), out heure);

    /// <summary>
    /// Construit l'intervalle d'une journée. Une fin strictement antérieure au début est replacée
    /// le lendemain (poste de nuit). Retourne <c>false</c> si les heures sont illisibles.
    /// </summary>
    public static bool TryIntervalle(DateTime jour, string? debutTxt, string? finTxt, out DateTime? ha, out DateTime? hd)
    {
        ha = null;
        hd = null;
        if (!TryHeure(debutTxt, out var debut) || !TryHeure(finTxt, out var fin))
            return false;
        ha = jour.Add(debut);
        hd = fin < debut ? jour.AddDays(1).Add(fin) : jour.Add(fin);
        return true;
    }

    /// <summary>
    /// Indice de la première cellule d'un jour : 2 (clé + lundi) puis un pas de
    /// 2 cellules (début, fin) ou 3 cellules (début, pause, fin) selon le format.
    /// </summary>
    public static int IndiceDebut(int jour, bool avecPauseParJour)
        => 2 + (avecPauseParJour ? 3 : 2) * jour;

    /// <summary>Lit une cellule d'une ligne découpée, en tolérant les lignes plus courtes.</summary>
    public static string? Cellule(string[] cells, int index)
        => index >= 0 && index < cells.Length ? cells[index] : null;
}
