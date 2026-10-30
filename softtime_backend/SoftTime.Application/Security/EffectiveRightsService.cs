using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Security;

/// <summary>Cache mémoire des droits effectifs, 30 secondes par compte. Une seule instance de l'application est supposée.</summary>
public sealed class EffectiveRightsCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<decimal, (DateTimeOffset At, EffectiveRights Rights)> _items = new();
    private readonly TimeProvider _time;

    public EffectiveRightsCache(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public bool TryGet(decimal userId, out EffectiveRights rights)
    {
        if (_items.TryGetValue(userId, out var entry))
        {
            if (_time.GetUtcNow() - entry.At < Lifetime)
            {
                rights = entry.Rights;
                return true;
            }
            _items.TryRemove(userId, out _);
        }
        rights = null!;
        return false;
    }

    public void Set(decimal userId, EffectiveRights rights) => _items[userId] = (_time.GetUtcNow(), rights);

    public void Invalidate(decimal userId) => _items.TryRemove(userId, out _);

    public void InvalidateAll() => _items.Clear();
}

/// <summary>
/// Lit en base ce qu'il faut pour calculer les droits effectifs d'un compte (étape 3B) et les met en cache.
/// Les erreurs de lecture remontent à l'appelant, qui doit REFUSER l'accès (échec fermé).
/// </summary>
public sealed class EffectiveRightsService
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
        var user = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(userId, ct);
        var roleEntity = user is null ? null : await _uow.Repository<T_ROLE>().GetByIdAsync(user.IDROLE, ct);

        var roleRows = new List<PermissionRow>();
        var roleGates = new List<int>();
        if (roleEntity is not null)
        {
            roleRows = (await _uow.Repository<T_ROLE_PERMISSION>().ListAsync(p => p.IDROLE == roleEntity.IDROLE, ct))
                .Select(p => new PermissionRow(p.PERMISSION_CODE, p.IS_GRANTED)).ToList();
            roleGates = (await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == roleEntity.IDROLE, ct))
                .Select(p => (int)p.IDDROIT).ToList();
        }
        var role = new RoleDefinition(roleEntity?.IDROLE, roleEntity is not null, roleRows, roleGates);

        var sages = await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, ct);
        var pointeuses = await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, ct);
        var accessIds = (await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == userId, ct))
            .Select(a => a.IDBDD).ToHashSet();
        var catalog = new DatabaseCatalog(
            sages.Select(s => s.NOM_BD ?? string.Empty).ToList(),
            pointeuses.Select(p => p.NOM_BD ?? string.Empty).ToList(),
            sages.Where(s => accessIds.Contains(s.ID)).Select(s => s.NOM_BD ?? string.Empty).ToList());

        var userRows = (await _uow.Repository<T_USER_PERMISSION>().ListAsync(p => p.IDRESPONSABLE == userId, ct))
            .Select(p => new PermissionRow(p.PERMISSION_CODE, p.IS_GRANTED)).ToList();

        return EffectiveRightsCalculator.ComputeEffectiveRights(role, LegacyToDeviations(role, userRows, catalog), catalog);
    }

    /// <summary>
    /// Étape 4b-5 : les lignes de l'utilisateur sont encore des listes complètes (modèle historique, toutes à IS_GRANTED = 1).
    /// On les convertit en écarts à la lecture pour que les droits effectifs restent ceux d'aujourd'hui.
    /// À supprimer à l'étape de migration vers le stockage en écarts.
    /// </summary>
    private static IReadOnlyCollection<PermissionRow> LegacyToDeviations(
        RoleDefinition role, List<PermissionRow> userRows, DatabaseCatalog catalog)
    {
        if (userRows.Count == 0 || userRows.Any(r => !r.IsGranted))
            return userRows;
        var roleOnly = EffectiveRightsCalculator.ComputeEffectiveRights(role, Array.Empty<PermissionRow>(), catalog);
        var (additions, removals) = EffectiveRightsCalculator.ConvertLegacyList(roleOnly.Permissions.ToList(), userRows.Select(r => r.Code).ToList());
        return additions.Select(c => new PermissionRow(c, true))
            .Concat(removals.Select(c => new PermissionRow(c, false)))
            .ToList();
    }

    /// <summary>Journal sans donnée sensible : identifiant du compte et type d'erreur seulement.</summary>
    public void LogReadFailure(decimal userId, Exception ex)
        => _logger.LogError("Lecture des droits effectifs impossible pour le compte {UserId} ({ErrorType}) : accès refusé.", userId, ex.GetType().Name);
}
