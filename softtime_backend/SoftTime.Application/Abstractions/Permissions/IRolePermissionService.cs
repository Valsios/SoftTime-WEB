using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Permissions;

public interface IRolePermissionService
{
    Task<RoleDetailedPermissionsDto> GetRoleDetailedPermissionsAsync(decimal roleId, CancellationToken cancellationToken = default);

    Task SaveRoleDetailedPermissionsAsync(decimal currentUserId, decimal targetRoleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default);
}
