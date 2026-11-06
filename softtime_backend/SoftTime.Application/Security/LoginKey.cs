using System.Linq.Expressions;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Security;

/// <summary>
/// Clé de comparaison d'un login : sans espaces en bordure et sans casse. Deux comptes ne peuvent pas
/// partager la même clé. La base l'impose aussi par la colonne calculée <c>T_RESPONSABLE.LOGIN_CLE</c>
/// et l'index unique <c>UX_T_RESPONSABLE_LOGIN_CLE</c> (migration 005), qui utilisent la même expression.
/// </summary>
public static class LoginKey
{
    /// <summary>Nom de l'index unique posé par la migration 005.</summary>
    public const string UniqueIndexName = "UX_T_RESPONSABLE_LOGIN_CLE";

    public const string TakenMessage = "Ce login est déjà utilisé. Choisissez-en un autre.";

    public static string Normalize(string? login) => (login ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Filtre traduit en SQL (<c>UPPER(LTRIM(RTRIM(LOGIN))) = @cle</c>) : une seule requête, sans charger la table.
    /// <paramref name="key"/> doit venir de <see cref="Normalize"/>.
    /// </summary>
    public static Expression<Func<T_RESPONSABLE, bool>> Matches(string key)
        => u => u.LOGIN != null && u.LOGIN.Trim().ToUpper() == key;

    /// <summary>Comme <see cref="Matches"/>, en excluant le compte <paramref name="exceptId"/>.</summary>
    public static Expression<Func<T_RESPONSABLE, bool>> MatchesOther(string key, decimal exceptId)
        => u => u.IDRESPONSABLE != exceptId && u.LOGIN != null && u.LOGIN.Trim().ToUpper() == key;
}
