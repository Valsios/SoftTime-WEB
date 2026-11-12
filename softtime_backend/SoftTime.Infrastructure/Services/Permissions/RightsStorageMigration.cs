using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Migration DROITS_ECARTS_V1, faite une seule fois au démarrage, en une seule transaction et sous verrou :
/// <list type="number">
///   <item>chaque rôle (hors administrateur) sans ligne reçoit les droits que ses portes lui accordaient (la porte Bases
///     devient la lecture de toutes les bases, joker « all ») ou le marqueur « aucun droit » ;</item>
///   <item>une action d'écran sans la page de son écran reçoit cette page (rôles et listes des comptes), car l'action
///     seule ne donne plus rien ;</item>
///   <item>chaque compte dont les lignes sont une liste complète (modèle historique) les voit remplacées par ses écarts
///     au rôle : ajouts (IS_GRANTED = 1) et retraits (IS_GRANTED = 0).</item>
/// </list>
/// Les droits effectifs de chaque compte restent ceux d'avant.
/// </summary>
public sealed class RightsStorageMigration : IRightsStorageMigration
{
    public const string Code = "DROITS_ECARTS_V1";

    public const string OldCodesMessage =
        "Des droits utilisent encore l'ancien format de code (« PARAMS:USERS ») : activez Database:AutoMigrate (développement) "
        + "ou exécutez migrations/versions/007_codes_permissions.sql avant de démarrer l'application.";

    private const string LockResource = "SoftTime.RightsStorageMigration";

    private readonly IUnitOfWork _uow;
    private readonly ILogger<RightsStorageMigration> _logger;

    public RightsStorageMigration(IUnitOfWork uow, ILogger<RightsStorageMigration> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public Task<RightsStorageMigrationResult> RunAsync(CancellationToken cancellationToken = default)
        => _uow.RunExclusiveAsync(LockResource, MigrateAsync, cancellationToken);

    private async Task<RightsStorageMigrationResult> MigrateAsync(CancellationToken ct)
    {
        if ((await _uow.Repository<DataMigration>().ListAsync(m => m.Code == Code, ct)).Count > 0)
            return new RightsStorageMigrationResult(true, 0, 0);

        await EnsureCodesRenamedAsync(ct);
        var roleRights = await ConvertRolesAsync(ct);
        var users = await ConvertUsersAsync(roleRights.Rights, ct);

        await _uow.Repository<DataMigration>().AddAsync(new DataMigration { Code = Code, DateApplication = DateTime.UtcNow }, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Migration des droits {Code} : {Roles} rôle(s) converti(s) en droits détaillés, {Users} compte(s) converti(s) en écarts.",
            Code, roleRights.Converted, users);
        return new RightsStorageMigrationResult(false, roleRights.Converted, users);
    }

    /// <summary>Lignes ajoutées aux rôles (portes converties, pages manquantes) ; retourne les droits de chaque rôle.</summary>
    private async Task<(Dictionary<decimal, List<string>> Rights, int Converted)> ConvertRolesAsync(CancellationToken ct)
    {
        var rows = await _uow.Repository<RolePermission>().ListAsync(_ => true, ct);
        var gates = await _uow.Repository<DroitRole>().ListAsync(_ => true, ct);
        var rights = new Dictionary<decimal, List<string>>();
        var converted = 0;
        foreach (var role in await _uow.Repository<Role>().ListAsync(r => r.IdRole != AdminRules.AdminRoleId, ct))
        {
            var own = rows.Where(r => r.IdRole == role.IdRole).ToList();
            var granted = own.Count > 0
                ? own.Where(r => r.IsGranted && r.PermissionCode != PermissionCatalog.EmptyRoleMarker).Select(r => r.PermissionCode).ToList()
                : EffectiveRightsCalculator.RightsFromLegacyGates(gates.Where(g => g.IdRole == role.IdRole).Select(g => (int)g.IdDroit).ToList()).ToList();
            var missing = MissingPages(granted);
            var toAdd = own.Count > 0 ? missing : granted.Concat(missing).DefaultIfEmpty(PermissionCatalog.EmptyRoleMarker).ToList();
            foreach (var code in toAdd)
                await _uow.Repository<RolePermission>().AddAsync(new RolePermission { IdRole = role.IdRole, PermissionCode = code, IsGranted = true }, ct);
            rights[role.IdRole] = granted.Concat(missing).ToList();
            if (own.Count == 0) converted++;
        }
        return (rights, converted);
    }

    /// <summary>Listes complètes des comptes remplacées par leurs écarts au rôle ; retourne le nombre de comptes convertis.</summary>
    private async Task<int> ConvertUsersAsync(IReadOnlyDictionary<decimal, List<string>> roleRights, CancellationToken ct)
    {
        var repo = _uow.Repository<UserPermission>();
        var converted = 0;
        foreach (var user in await _uow.Repository<Responsable>().ListAsync(u => u.IdRole != AdminRules.AdminRoleId, ct))
        {
            var rows = await repo.ListAsync(p => p.IdResponsable == user.IdResponsable, ct);
            if (!IsLegacyList(rows) || !roleRights.TryGetValue(user.IdRole, out var role))
                continue;
            var list = rows.Select(r => r.PermissionCode).ToList();
            var (additions, removals) = EffectiveRightsCalculator.ConvertLegacyList(role, list.Concat(MissingPages(list)).ToList());
            repo.RemoveRange(rows);
            foreach (var code in additions)
                await repo.AddAsync(new UserPermission { IdResponsable = user.IdResponsable, PermissionCode = code, IsGranted = true }, ct);
            foreach (var code in removals)
                await repo.AddAsync(new UserPermission { IdResponsable = user.IdResponsable, PermissionCode = code, IsGranted = false }, ct);
            converted++;
        }
        return converted;
    }

    /// <summary>Pages d'écran absentes alors qu'une de leurs actions est présente (« categories.read » pour « categories.delete »).</summary>
    private static List<string> MissingPages(IReadOnlyCollection<string> codes)
    {
        var present = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
        return codes.Select(PermissionCatalog.PageOf)
            .Where(page => page is not null && !present.Contains(page))
            .Select(page => page!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Liste complète historique : des lignes, toutes accordées, autres que le seul marqueur « aucun droit ».</summary>
    private static bool IsLegacyList(IReadOnlyCollection<UserPermission> rows)
        => rows.Count > 0
           && rows.All(r => r.IsGranted)
           && rows.Any(r => r.PermissionCode != PermissionCatalog.EmptyUserMarker);

    /// <summary>
    /// Codes encore à l'ancien format (« PARAMS:USERS », « SAGE:BASE:READ ») : la migration 007 n'a pas été appliquée.
    /// Convertir maintenant ferait perdre leurs droits aux comptes (codes inconnus) : le démarrage s'arrête.
    /// </summary>
    private async Task EnsureCodesRenamedAsync(CancellationToken ct)
    {
        var roleOld = await _uow.Repository<RolePermission>().ListAsync(p => p.PermissionCode.Contains(":") && !p.PermissionCode.StartsWith("__"), ct);
        var userOld = await _uow.Repository<UserPermission>().ListAsync(p => p.PermissionCode.Contains(":") && !p.PermissionCode.StartsWith("__"), ct);
        if (roleOld.Count + userOld.Count > 0)
            throw new DomainException(OldCodesMessage);
    }
}
