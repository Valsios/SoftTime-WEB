using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Renomme ou retire les codes <c>&lt;prefix&gt;:&lt;nom&gt;:&lt;action&gt;</c> d'une base dans T_ROLE_PERMISSION et
/// T_USER_PERMISSION, sans enregistrer (l'appelant fait un seul SaveChangesAsync).
/// </summary>
public sealed class DatabasePermissionCodeMigrator : IDatabasePermissionCodeMigrator
{
    private readonly PermissionTable<RolePermission> _roles;
    private readonly PermissionTable<UserPermission> _users;

    public DatabasePermissionCodeMigrator(IUnitOfWork uow)
    {
        _roles = new PermissionTable<RolePermission>(uow.Repository<RolePermission>(), RoleRows());
        _users = new PermissionTable<UserPermission>(uow.Repository<UserPermission>(), UserRows());
    }

    public async Task RenameAsync(string prefix, string oldName, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)
            || string.Equals(oldName, newName, StringComparison.Ordinal))
            return;
        await _roles.RenameAsync(prefix, oldName, newName, ct);
        await _users.RenameAsync(prefix, oldName, newName, ct);
    }

    public async Task RemoveAsync(string prefix, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await _roles.RemoveAsync(prefix, name, ct);
        await _users.RemoveAsync(prefix, name, ct);
    }

    private static PermissionRowMap<RolePermission> RoleRows() => new()
    {
        Owner = r => r.IdRole,
        Code = r => r.PermissionCode,
        SetCode = (r, code) => r.PermissionCode = code,
        CodeStartsWith = start => r => r.PermissionCode.StartsWith(start),
        OwnedBy = ids => r => ids.Contains(r.IdRole),
        EmptyMarker = id => new RolePermission
        {
            IdRole = id, PermissionCode = PermissionMarkers.EmptyRolePermissionsMarker, IsGranted = true,
        },
    };

    private static PermissionRowMap<UserPermission> UserRows() => new()
    {
        Owner = r => r.IdResponsable,
        Code = r => r.PermissionCode,
        SetCode = (r, code) => r.PermissionCode = code,
        CodeStartsWith = start => r => r.PermissionCode.StartsWith(start),
        OwnedBy = ids => r => ids.Contains(r.IdResponsable),
        EmptyMarker = id => new UserPermission
        {
            IdResponsable = id, PermissionCode = PermissionMarkers.EmptyPermissionsMarker, IsGranted = true,
        },
    };
}
