namespace SoftTime.Application.Abstractions;

public static class CurrentUserExtensions
{
    /// <summary>Identifiant du compte connecté ; <see cref="UnauthorizedException"/> (401) si le jeton n'en porte pas.</summary>
    public static decimal RequireUserId(this ICurrentUser user)
        => user.UserId ?? throw new UnauthorizedException("Utilisateur non identifié.");
}
