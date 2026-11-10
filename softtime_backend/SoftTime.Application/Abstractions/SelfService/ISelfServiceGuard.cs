namespace SoftTime.Application.Abstractions.SelfService;

public interface ISelfServiceGuard
{
    /// <summary>
    /// Résout le salarié connecté : permission <c>SELF:READ</c> effective (aucune porte requise), matricule du jeton
    /// égal au matricule courant du compte et porté par lui seul, une seule base autorisée et une carte dans cette
    /// base. Tout refus est une <c>ForbiddenException</c> (403) au message explicite.
    /// </summary>
    Task<SelfContext> ResolveAsync(CancellationToken ct = default);
}
