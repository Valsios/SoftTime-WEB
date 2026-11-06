using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IUserAdministrationService
{
    Task<UserDto> SaveUserAsync(decimal currentUserId, UserDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aperçu d'un changement de rôle : ajustements personnels qui seraient effacés. Lecture seule, administrateur seulement.
    /// Si le rôle ne change pas ou si le compte n'a aucun ajustement, Count vaut 0.
    /// </summary>
    Task<RoleChangePreviewDto> PreviewRoleChangeAsync(decimal userId, decimal newRoleId, CancellationToken cancellationToken = default);
}
