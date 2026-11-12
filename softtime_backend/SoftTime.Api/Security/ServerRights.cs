namespace SoftTime.Api.Security;

/// <summary>
/// Rôle et portes du compte connecté lus en base pour la requête en cours (droits effectifs, cache de 30 s vidé à chaque
/// modification de droits). Ils remplacent le rôle et les portes du jeton, qui peuvent dater de 30 minutes.
/// </summary>
public sealed record ServerRights(decimal? RoleId, IReadOnlyCollection<int> Gates)
{
    public const string ItemKey = "SoftTime.ServerRights";

    /// <summary>Aucun droit : compte introuvable, lecture impossible (échec fermé) ou middleware pas encore passé.</summary>
    public static readonly ServerRights None = new(null, Array.Empty<int>());
}
