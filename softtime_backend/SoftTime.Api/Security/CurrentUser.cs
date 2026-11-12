using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Services;

namespace SoftTime.Api.Security;

/// <summary>
/// Compte de la requête. Identité (identifiant, login, matricule) lue dans le jeton ; rôle et portes lus en base par
/// <see cref="Middleware.ServerRightsMiddleware"/> quand <see cref="Server"/> est fourni (requête HTTP authentifiée),
/// sinon ceux de <see cref="TokenRoleId"/> / <see cref="TokenRights"/> (identité système des tâches de fond).
/// </summary>
public class CurrentUser : ICurrentUser
{
    public decimal? UserId { get; init; }
    public string? Login { get; init; }
    public string? Matricule { get; init; }
    public decimal? TokenRoleId { get; init; }
    public IReadOnlyCollection<int> TokenRights { get; init; } = Array.Empty<int>();

    /// <summary>Droits lus en base pour la requête ; null = utiliser le rôle et les portes du jeton.</summary>
    public Func<ServerRights>? Server { get; init; }

    public decimal? RoleId => Server is null ? TokenRoleId : Server().RoleId;
    public IReadOnlyCollection<int> Rights => Server is null ? TokenRights : Server().Gates;
    public bool HasRight(int droit) => RoleId == AdminRules.AdminRoleId || Rights.Contains(droit);
}
