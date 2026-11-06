using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IFieldRoleService
{
    Task<IReadOnlyList<FieldRoleDto>> GetFieldRolesAsync(string? systemType = null, CancellationToken ct = default);
}
