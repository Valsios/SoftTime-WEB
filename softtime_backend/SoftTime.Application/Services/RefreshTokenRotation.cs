namespace SoftTime.Application.Services;

/// <summary>Résultat d'une rotation : compte concerné et nouveau jeton (valeur en clair, jamais stockée).</summary>
public sealed record RefreshTokenRotation(decimal UserId, string RefreshToken);
