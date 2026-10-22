namespace SoftTime.Domain.Services;

/// <summary>
/// Motifs d'anomalie de pointage. Valeurs stables (stockées dans <c>T_HEUREANOMALIE.MOTIF</c> et
/// exposées par l'API) : ne pas les renommer sans migration.
/// </summary>
public static class PunchAnomalyMotifs
{
    /// <summary>Aucun pointage sur un jour travaillé (absence probable).</summary>
    public const string AucunPointage = "AUCUN_POINTAGE";

    /// <summary>Un seul pointage : la sortie manque.</summary>
    public const string PointageUnique = "POINTAGE_UNIQUE";

    /// <summary>Nombre ou enchaînement de pointages inexploitable (3, 5, 6… pointages).</summary>
    public const string PointagesIncoherents = "POINTAGES_INCOHERENTS";

    /// <summary>Durée de la journée hors des bornes plausibles configurées.</summary>
    public const string Duree = "DUREE";

    /// <summary>Pointage un jour non planifié (week-end, <c>OffP</c>, absence de planning).</summary>
    public const string HorsPlanning = "HORS_PLANNING";
}
