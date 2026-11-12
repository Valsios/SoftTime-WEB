using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Lit en base ce qu'il faut pour calculer les droits effectifs d'un compte (étape 3B) et les met en cache.
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
        var rights = await LoadAsync(userId, ct);
        _cache.Set(userId, rights);
        return rights;
    }

    private async Task<EffectiveRights> LoadAsync(decimal userId, CancellationToken ct)
    {
        var (role, rows, catalog) = await LoadInputsAsync(userId, ct);
        return EffectiveRightsCalculator.ComputeEffectiveRights(role, rows, catalog);
    }

    /// <summary>
    /// Ajustements personnels réels d'un compte par rapport à son rôle actuel : droits ajoutés (« +CODE ») et retirés (« -CODE »).
    /// C'est ce qu'un changement de rôle effacerait ; lecture seule.
    /// </summary>
    public async Task<IReadOnlyList<string>> AdjustmentsAsync(decimal userId, CancellationToken ct = default)
    {
        var (role, rows, catalog) = await LoadInputsAsync(userId, ct);
        var roleOnly = EffectiveRightsCalculator.ComputeEffectiveRights(role, Array.Empty<PermissionRow>(), catalog);
        var effective = EffectiveRightsCalculator.ComputeEffectiveRights(role, rows, catalog);
        var (additions, removals) = EffectiveRightsCalculator.ConvertLegacyList(roleOnly.Permissions.ToList(), effective.Permissions.ToList());
        return additions.Select(c => "+" + c).Concat(removals.Select(c => "-" + c)).ToList();
    }

    /// <summary>
    /// Droits effectifs d'un rôle pris seul (sans ajustement de compte), tels que le calculateur les accorde :
    /// lignes détaillées du rôle, ou droits par défaut de ses portes SANS la configuration des bases.
    /// Sert à afficher les droits d'un rôle sans lignes : l'écran montre exactement ce que le serveur accorde.
    /// </summary>
    public async Task<IReadOnlyList<string>> RoleDefaultPermissionsAsync(decimal roleId, CancellationToken ct = default)
    {
        var roleEntity = await _uow.Repository<Role>().GetByIdAsync(roleId, ct);
        var rows = (await _uow.Repository<RolePermission>().ListReadOnlyAsync(p => p.IdRole == roleId, ct))
            .Select(p => new PermissionRow(p.PermissionCode, p.IsGranted)).ToList();
        var gates = (await _uow.Repository<DroitRole>().ListReadOnlyAsync(p => p.IdRole == roleId, ct))
            .Select(p => (int)p.IdDroit).ToList();
        var role = new RoleDefinition(roleId, roleEntity is not null || roleId == AdminRules.AdminRoleId, rows, gates);

        var sages = (await _uow.Repository<BddSage>().ListReadOnlyAsync(_ => true, ct)).Select(s => s.NomBd ?? string.Empty).ToList();
        var pointeuses = (await _uow.Repository<BddPointeuse>().ListReadOnlyAsync(_ => true, ct)).Select(p => p.NomBd ?? string.Empty).ToList();
        var catalog = new DatabaseCatalog(sages, pointeuses, sages);
        return EffectiveRightsCalculator.ComputeEffectiveRights(role, Array.Empty<PermissionRow>(), catalog).Permissions.ToList();
    }

    private async Task<(RoleDefinition Role, IReadOnlyCollection<PermissionRow> Rows, DatabaseCatalog Catalog)> LoadInputsAsync(decimal userId, CancellationToken ct)
    {
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, ct);
        var roleEntity = user is null ? null : await _uow.Repository<Role>().GetByIdAsync(user.IdRole, ct);

        var roleRows = new List<PermissionRow>();
        var roleGates = new List<int>();
        if (roleEntity is not null)
        {
            roleRows = (await _uow.Repository<RolePermission>().ListAsync(p => p.IdRole == roleEntity.IdRole, ct))
                .Select(p => new PermissionRow(p.PermissionCode, p.IsGranted)).ToList();
            roleGates = (await _uow.Repository<DroitRole>().ListAsync(p => p.IdRole == roleEntity.IdRole, ct))
                .Select(p => (int)p.IdDroit).ToList();
        }
        var role = new RoleDefinition(roleEntity?.IdRole, roleEntity is not null, roleRows, roleGates);

        var sages = await _uow.Repository<BddSage>().ListAsync(_ => true, ct);
        var pointeuses = await _uow.Repository<BddPointeuse>().ListAsync(_ => true, ct);
        var accessIds = (await _uow.Repository<BddAutorisee>().ListAsync(a => a.IdResponsable == userId, ct))
            .Select(a => a.IdBdd).ToHashSet();
        var catalog = new DatabaseCatalog(
            sages.Select(s => s.NomBd ?? string.Empty).ToList(),
            pointeuses.Select(p => p.NomBd ?? string.Empty).ToList(),
            sages.Where(s => accessIds.Contains(s.Id)).Select(s => s.NomBd ?? string.Empty).ToList());

        var stored = await _uow.Repository<UserPermission>().ListAsync(p => p.IdResponsable == userId, ct);
        // Depuis la migration 006, les lignes sont des écarts. Une ancienne copie non convertie serait lue comme des
        // ajouts : refus (l'appelant refuse l'accès) plutôt qu'un droit faux. La conversion a lieu au démarrage.
        if (stored.Any(p => !p.IsDeviation))
            throw new InvalidOperationException(
                $"Droits du compte {userId} non convertis en écarts (migration 006) : accès refusé jusqu'au redémarrage de l'API.");
        var userRows = stored.Select(p => new PermissionRow(p.PermissionCode, p.IsGranted)).ToList();

        return (role, userRows, catalog);
    }

    /// <summary>Journal sans donnée sensible : identifiant du compte et type d'erreur seulement.</summary>
    public void LogReadFailure(decimal userId, Exception ex)
        => _logger.LogError("Lecture des droits effectifs impossible pour le compte {UserId} ({ErrorType}) : accès refusé.", userId, ex.GetType().Name);
}
