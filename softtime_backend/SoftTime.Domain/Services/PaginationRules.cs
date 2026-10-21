namespace SoftTime.Domain.Services;

/// <summary>
/// Normalisation des paramètres de pagination. Les listes massives (pointages, heures corrigées)
/// peuvent porter plusieurs dizaines de milliers de lignes sur une période : la pagination est
/// bornée pour protéger le serveur.
/// </summary>
public static class PaginationRules
{
    /// <summary>Taille de page utilisée quand le client n'en demande pas.</summary>
    public const int TailleDefaut = 50;

    /// <summary>Taille de page maximale acceptée : toute demande supérieure est ramenée à cette borne.</summary>
    public const int TailleMax = 100;

    /// <summary>
    /// Ramène la page demandée à au moins 1 et la taille de page dans l'intervalle
    /// <c>1..<see cref="TailleMax"/></c>, en retombant sur <see cref="TailleDefaut"/> si elle est absente.
    /// </summary>
    public static (int Page, int Taille) Normaliser(int? page, int? taille)
    {
        var pageNormalisee = page is null or < 1 ? 1 : page.Value;
        var tailleNormalisee = taille is null or < 1 ? TailleDefaut : Math.Min(taille.Value, TailleMax);
        return (pageNormalisee, tailleNormalisee);
    }

    /// <summary>Indice de départ (0-based) d'une page, pour un <c>Skip</c>.</summary>
    /// <remarks>
    /// Le calcul est effectué en <see cref="long"/> : une page extrême (<c>int.MaxValue</c>) ne doit
    /// jamais produire un offset négatif par débordement. L'offset est plafonné à
    /// <see cref="int.MaxValue"/>, valeur maximale acceptée par <c>Queryable.Skip(int)</c>.
    /// </remarks>
    public static int Decalage(int page, int taille)
    {
        var pageNormalisee = page < 1 ? 1 : page;
        var tailleNormalisee = taille < 1 ? 0 : taille;
        var offset = (long)(pageNormalisee - 1) * tailleNormalisee;
        return offset > int.MaxValue ? int.MaxValue : (int)offset;
    }

    /// <summary>
    /// Page effective à exposer pour un résultat vide : une liste vide n'a qu'une page, la page 1.
    /// Pour un total non nul, la page demandée est conservée (le bornage est fait par l'appelant).
    /// </summary>
    public static int PageEffective(int total, int page) => total <= 0 ? 1 : page;

    /// <summary>Nombre de pages nécessaires pour <paramref name="total"/> lignes (au moins 1).</summary>
    public static int NombreDePages(int total, int taille)
    {
        if (total <= 0 || taille <= 0)
            return 1;
        return (int)Math.Ceiling(total / (double)taille);
    }
}
