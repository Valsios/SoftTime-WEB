namespace SoftTime.Application.Services;

/// <summary>Lecture du filtre par type d'anomalie de la clôture : types séparés par des virgules, insensibles à la
/// casse. Même contrat que <see cref="ClosingSeverityFilter"/> : la LISTE seule est filtrée, jamais les comptages,
/// le score ni le statut.</summary>
public static class ClosingTypeFilter
{
    public const string Message =
        "Le paramètre types doit lister des types d'anomalie connus (par exemple PointageManquant, QuasiDoublon).";

    /// <summary>Absent ou vide : aucun filtre (<c>null</c>) ; valeur inconnue : <c>false</c>. Les doublons sont
    /// ignorés.</summary>
    public static bool TryParse(string? valeur, out IReadOnlyList<ClosingAnomalyType>? types)
    {
        types = null;
        if (string.IsNullOrWhiteSpace(valeur))
            return true;
        var liste = new List<ClosingAnomalyType>();
        const StringSplitOptions decoupe = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        foreach (var morceau in valeur.Split(',', decoupe))
        {
            if (!Enum.TryParse<ClosingAnomalyType>(morceau, ignoreCase: true, out var type) || !Enum.IsDefined(type))
                return false;
            if (!liste.Contains(type))
                liste.Add(type);
        }
        types = liste.Count > 0 ? liste : null;
        return true;
    }

    /// <summary>Types d'une valeur déjà validée (voir <c>ClosingAnalysisQueryValidator</c>).</summary>
    public static IReadOnlyList<ClosingAnomalyType>? Parse(string? valeur) =>
        TryParse(valeur, out var types) ? types : throw new DomainException(Message);
}
