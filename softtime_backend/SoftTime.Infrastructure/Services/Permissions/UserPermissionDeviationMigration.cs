using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Convertit les copies complètes en écarts avec la même règle que l'ancienne lecture (droits du rôle seul, puis
/// EffectiveRightsCalculator.ConvertLegacyList) : les droits effectifs de chaque compte restent identiques.
/// <list type="bullet">
///   <item>des lignes contenant déjà un retrait (IS_GRANTED = 0) étaient lues comme des écarts : elles sont seulement
///     marquées comme écarts ;</item>
///   <item>codes inconnus et marqueur « aucun droit » : ignorés par l'ancienne lecture, ils ne sont pas repris ;</item>
///   <item>administrateur : le calcul ignore ses lignes ; seuls les ajouts (ex. SELF:READ) sont conservés.</item>
/// </list>
/// </summary>
public sealed class UserPermissionDeviationMigration : IUserPermissionDeviationMigration
{
    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _rights;
    private readonly ILogger<UserPermissionDeviationMigration> _logger;

    public UserPermissionDeviationMigration(
        IUnitOfWork uow, IEffectiveRightsService rights, ILogger<UserPermissionDeviationMigration> logger)
    {
        _uow = uow;
        _rights = rights;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var repo = _uow.Repository<UserPermission>();
        var legacyOwners = (await repo.ListAsync(p => !p.IsDeviation, ct))
            .Select(p => p.IdResponsable)
            .Distinct()
            .ToList();
        if (legacyOwners.Count == 0)
            return 0;

        var roleOnlyCache = new Dictionary<decimal, IReadOnlyList<string>>();
        foreach (var userId in legacyOwners)
            await ConvertAsync(repo, userId, roleOnlyCache, ct);

        await _uow.SaveChangesAsync(ct);
        _rights.InvalidateAll();
        _logger.LogInformation("Migration 006 : droits de {Count} compte(s) convertis en écarts.", legacyOwners.Count);
        return legacyOwners.Count;
    }

    private async Task ConvertAsync(
        IRepository<UserPermission> repo,
        decimal userId,
        Dictionary<decimal, IReadOnlyList<string>> roleOnlyCache,
        CancellationToken ct)
    {
        var rows = await repo.ListAsync(p => p.IdResponsable == userId, ct);
        if (rows.Any(r => !r.IsGranted))
        {
            foreach (var row in rows)
            {
                row.IsDeviation = true;
                repo.Update(row);
            }
            return;
        }

        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, ct);
        var roleOnly = user is null ? Array.Empty<string>() : await RoleOnlyAsync(user.IdRole, roleOnlyCache, ct);
        var codes = rows.Select(r => r.PermissionCode).ToList();
        var (additions, removals) = EffectiveRightsCalculator.ConvertLegacyList(roleOnly, codes);
        if (user is not null && AdminRules.IsSuperAdmin(user.IdRole))
            removals = Array.Empty<string>();

        repo.RemoveRange(rows);
        var deviations = additions.Select(c => (Code: c, Granted: true))
            .Concat(removals.Select(c => (Code: c, Granted: false)));
        foreach (var (code, granted) in deviations)
            await repo.AddAsync(new UserPermission { IdResponsable = userId, PermissionCode = code, IsGranted = granted }, ct);
    }

    private async Task<IReadOnlyList<string>> RoleOnlyAsync(
        decimal roleId, Dictionary<decimal, IReadOnlyList<string>> cache, CancellationToken ct)
    {
        if (!cache.TryGetValue(roleId, out var perms))
        {
            perms = await _rights.RoleDefaultPermissionsAsync(roleId, ct);
            cache[roleId] = perms;
        }
        return perms;
    }
}
