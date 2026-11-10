using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Permissions;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<RoleDto>> GetRolesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default);

    Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default);
}
