namespace SoftTime.Application.Services;

/// <summary>Lecture du filtre de gravité de la clôture : niveaux séparés par des virgules, insensibles à la
/// casse.</summary>
public static class ClosingSeverityFilter
{
    public const string Message =
        "Le paramètre gravites doit lister des niveaux parmi Information, Mineure, Importante, Critique.";

    /// <summary>Absent ou vide : aucun filtre (<c>null</c>) ; valeur inconnue : <c>false</c>. Les doublons sont
    /// ignorés.</summary>
    public static bool TryParse(string? valeur, out IReadOnlyList<ClosingSeverity>? niveaux)
    {
        niveaux = null;
        if (string.IsNullOrWhiteSpace(valeur))
            return true;
        var liste = new List<ClosingSeverity>();
        const StringSplitOptions decoupe = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        foreach (var morceau in valeur.Split(',', decoupe))
        {
            if (!Enum.TryParse<ClosingSeverity>(morceau, ignoreCase: true, out var niveau) || !Enum.IsDefined(niveau))
                return false;
            if (!liste.Contains(niveau))
                liste.Add(niveau);
        }
        niveaux = liste.Count > 0 ? liste : null;
        return true;
    }

    /// <summary>Niveaux d'une valeur déjà validée (voir <c>ClosingAnalysisQueryValidator</c>).</summary>
    public static IReadOnlyList<ClosingSeverity>? Parse(string? valeur) =>
        TryParse(valeur, out var niveaux) ? niveaux : throw new DomainException(Message);
}
