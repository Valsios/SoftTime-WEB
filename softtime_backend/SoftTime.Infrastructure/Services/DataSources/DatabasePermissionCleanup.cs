using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.DataSources;

/// <summary>
/// Suppression d'une base : ses droits nominatifs (« sage.12.read »…) sont retirés des rôles et des comptes, pour qu'une
/// base créée plus tard ne puisse jamais hériter d'un identifiant réutilisé. Les jokers « all » sont conservés.
/// Enregistré avec la sauvegarde de l'appelant.
/// </summary>
internal static class DatabasePermissionCleanup
{
    public static async Task RemoveAsync(IUnitOfWork uow, DatabaseKind kind, int id, CancellationToken ct)
    {
        var prefix = $"{DatabasePermissionCodes.Prefix(kind)}.{id}.";
        var roleRows = uow.Repository<RolePermission>();
        roleRows.RemoveRange(await roleRows.ListAsync(p => p.PermissionCode.StartsWith(prefix), ct));
        var userRows = uow.Repository<UserPermission>();
        userRows.RemoveRange(await userRows.ListAsync(p => p.PermissionCode.StartsWith(prefix), ct));
    }
}
