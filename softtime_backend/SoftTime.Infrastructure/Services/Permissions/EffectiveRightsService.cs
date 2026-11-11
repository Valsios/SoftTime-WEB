using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Lit en base ce qu'il faut pour calculer les droits effectifs d'un compte et les met en cache.
/// Les lignes du compte sont des écarts au rôle (IS_GRANTED = 1 : ajout, 0 : retrait), voir RightsStorageMigration.
/// Les erreurs de lecture remontent à l'appelant, qui doit REFUSER l'accès (échec fermé).
/// </summary>
public sealed class EffectiveRightsService : IEffectiveRightsService
{
    private readonly IUnitOfWork _uow;
    private readonly EffectiveRightsCache _cache;
    private readonly ILogger<EffectiveRightsService> _logger;

    public EffectiveRightsService(IUnitOfWork uow, EffectiveRightsCache cache, ILogger<EffectiveRightsService> logger)
    {
        _uow = uow;
        _cache = cache;
        _logger = logger;
    }

    public void Invalidate(decimal userId) => _cache.Invalidate(userId);

    public void InvalidateAll() => _cache.InvalidateAll();

    public async Task<EffectiveRights> GetAsync(decimal userId, bool fresh = false, CancellationToken ct = default)
    {
        if (!fresh && _cache.TryGet(userId, out var cached))
            return cached;
        var (role, rows, catalog) = await LoadInputsAsync(userId, ct);
        var rights = EffectiveRightsCalculator.ComputeEffectiveRights(role, rows, catalog);
        _cache.Set(userId, rights);
        return rights;
    }

    public async Task<EffectiveRights> RoleOnlyAsync(decimal userId, CancellationToken ct = default)
    {
        var (role, _, catalog) = await LoadInputsAsync(userId, ct);
        return EffectiveRightsCalculator.ComputeEffectiveRights(role, Array.Empty<PermissionRow>(), catalog);
    }

    public async Task<IReadOnlyList<string>> AdjustmentsAsync(decimal userId, CancellationToken ct = default)
    {
        var (role, rows, catalog) = await LoadInputsAsync(userId, ct);
        var roleOnly = EffectiveRightsCalculator.ComputeEffectiveRights(role, Array.Empty<PermissionRow>(), catalog);
        var effective = EffectiveRightsCalculator.ComputeEffectiveRights(role, rows, catalog);
        var (additions, removals) = EffectiveRightsCalculator.ConvertLegacyList(roleOnly.Permissions.ToList(), effective.Permissions.ToList());
        return additions.Select(c => "+" + c).Concat(removals.Select(c => "-" + c)).ToList();
    }

    public void LogReadFailure(decimal userId, Exception ex)
        => _logger.LogError("Lecture des droits effectifs impossible pour le compte {UserId} ({ErrorType}) : accès refusé.", userId, ex.GetType().Name);

    private async Task<(RoleDefinition Role, IReadOnlyCollection<PermissionRow> Rows, DatabaseCatalog Catalog)> LoadInputsAsync(
        decimal userId, CancellationToken ct)
    {
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, ct);
        var roleEntity = user is null ? null : await _uow.Repository<Role>().GetByIdAsync(user.IdRole, ct);
        var roleRows = roleEntity is null
            ? new List<PermissionRow>()
            : (await _uow.Repository<RolePermission>().ListAsync(p => p.IdRole == roleEntity.IdRole, ct))
                .Select(p => new PermissionRow(p.PermissionCode, p.IsGranted)).ToList();
        var role = new RoleDefinition(roleEntity?.IdRole, roleEntity is not null, roleRows);

        var sages = await _uow.Repository<BddSage>().ListAsync(_ => true, ct);
        var pointeuses = await _uow.Repository<BddPointeuse>().ListAsync(_ => true, ct);
        var accessIds = (await _uow.Repository<BddAutorisee>().ListAsync(a => a.IdResponsable == userId, ct))
            .Select(a => a.IdBdd).ToHashSet();
        var catalog = new DatabaseCatalog(
            sages.Select(s => new DatabaseRef(s.Id, s.NomBd ?? string.Empty)).ToList(),
            pointeuses.Select(p => new DatabaseRef(p.Id, p.NomBd ?? string.Empty)).ToList(),
            sages.Where(s => accessIds.Contains(s.Id)).Select(s => s.Id).ToList());

        var userRows = (await _uow.Repository<UserPermission>().ListAsync(p => p.IdResponsable == userId, ct))
            .Select(p => new PermissionRow(p.PermissionCode, p.IsGranted)).ToList();
        return (role, userRows, catalog);
    }
}
