using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;

namespace SoftTime.Application.Services;

/// <summary>
/// Listes des bases filtrées pour le compte connecté : un compte aux droits détaillés ne voit que les bases
/// qu'il peut lire (<c>SAGE:{base}:READ</c>, <c>POINTEUSE:{base}:READ</c>) ; sinon, toutes les bases.
/// </summary>
public class DatabaseListingService : IDatabaseListingService
{
    private const string Sage = "SAGE";
    private const string Pointeuse = "POINTEUSE";

    private readonly ICatalogService _catalog;
    private readonly IAuthService _auth;
    private readonly ICurrentUser _user;

    public DatabaseListingService(ICatalogService catalog, IAuthService auth, ICurrentUser user)
    {
        _catalog = catalog;
        _auth = auth;
        _user = user;
    }

    public async Task<IReadOnlyList<SageDbDto>> ListSageAsync(CancellationToken ct = default)
    {
        var list = await _catalog.ListSageAsync(ct);
        var allowed = await AllowedNamesAsync(Sage, ct);
        return allowed is null ? list : list.Where(s => !string.IsNullOrEmpty(s.NomBd) && allowed.Contains(s.NomBd)).ToList();
    }

    public async Task<PagedResult<SageDbDto>> ListSagePagedAsync(int? page, int? pageSize, string? search, CancellationToken ct = default)
        => await _catalog.ListSagePagedAsync(await AllowedNamesAsync(Sage, ct), page, pageSize, search, ct);

    public async Task<IReadOnlyList<PointeuseDbDto>> ListPointeuseAsync(CancellationToken ct = default)
    {
        var list = await _catalog.ListPointeuseAsync(ct);
        var allowed = await AllowedNamesAsync(Pointeuse, ct);
        return allowed is null ? list : list.Where(p => !string.IsNullOrEmpty(p.NomBd) && allowed.Contains(p.NomBd)).ToList();
    }

    public async Task<PagedResult<PointeuseDbDto>> ListPointeusePagedAsync(int? page, int? pageSize, string? search, CancellationToken ct = default)
        => await _catalog.ListPointeusePagedAsync(await AllowedNamesAsync(Pointeuse, ct), page, pageSize, search, ct);

    /// <summary>Bases lisibles du compte ; null quand aucun filtre ne s'applique (droits non personnalisés, ou appel système).</summary>
    private async Task<IReadOnlyCollection<string>?> AllowedNamesAsync(string system, CancellationToken ct)
    {
        if (_user.UserId is not decimal uid)
            return null;
        var permissions = await _auth.GetUserDetailedPermissionsAsync(uid, ct);
        return permissions.IsCustomized ? DatabasePermissionNames.Read(permissions.GrantedPermissions, system) : null;
    }
}
