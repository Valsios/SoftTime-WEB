using System.Globalization;

namespace SoftTime.Domain.Services;

/// <summary>
/// Identité d'un matricule salarié : les bases SAGE et pointage ne remplissent pas toujours les
/// zéros de la même façon (« 00369 » et « 369 » désignent le même salarié). Cette classe fournit
/// une clé canonique, utilisable comme clé de dictionnaire, pour comparer sans se tromper.
/// </summary>
public static class MatriculeIdentity
{
    /// <summary>
    /// Clé canonique : un matricule numérique est ramené à sa valeur entière (« 00369 » → « 369 »),
    /// un matricule alphanumérique est simplement nettoyé et mis en majuscules.
    /// </summary>
    public static string Cle(string? matricule)
    {
        var valeur = matricule?.Trim() ?? string.Empty;
        return long.TryParse(valeur, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nombre)
            ? nombre.ToString(CultureInfo.InvariantCulture)
            : valeur.ToUpperInvariant();
    }

    /// <summary>Deux matricules désignent-ils le même salarié ?</summary>
    public static bool MemeSalarie(string? gauche, string? droite)
    {
        var cle = Cle(gauche);
        return cle.Length > 0 && cle == Cle(droite);
    }

    /// <summary>
    /// Comparaison de matricules ou de badges insensible au remplissage par des zéros
    /// (« 00369 » == « 369 ») ; deux valeurs vides ne sont jamais égales.
    /// </summary>
    public static bool MemeIdentifiant(string? a, string? b)
    {
        a = a?.Trim();
        b = b?.Trim();
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        if (long.TryParse(a, out var na) && long.TryParse(b, out var nb))
            return na == nb;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
