using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Permissions;

public interface IPrivilegeService
{
    /// <summary>Catalogue des permissions affiché par l'écran Privilèges (même source que la validation serveur).</summary>
    IReadOnlyList<PermissionCategoryDto> GetCatalog();

    Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default);
}
