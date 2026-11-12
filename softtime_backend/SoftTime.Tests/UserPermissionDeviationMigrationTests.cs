using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Migration 006 : les copies complètes des droits des comptes deviennent des écarts par rapport au rôle, sans changer
/// les droits effectifs d'aucun compte. La référence « avant » est l'ancienne lecture (copie convertie en écarts à la
/// volée), reproduite ici à l'identique.
/// </summary>
public class UserPermissionDeviationMigrationTests
{
    private const string UserMarker = "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__";

    private readonly InMemoryUnitOfWork _uow = new();

    public UserPermissionDeviationMigrationTests()
    {
        foreach (var (id, name) in new[] { (1m, "Administrateur"), (10m, "Hérité"), (11m, "Configuré"), (12m, "Vide"), (13m, "Salarié") })
            _uow.Get<Role>().Items.Add(new Role { IdRole = id, NomRole = name });
        foreach (var gate in new[] { 1, 3 })
            _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 10, IdDroit = gate });
        foreach (var code in new[] { "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "SAGE:BASE1:READ" })
            _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 11, PermissionCode = code, IsGranted = true });
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 12, PermissionCode = "__SOFTTIME_EMPTY_ROLE_PERMISSIONS__", IsGranted = true });
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 13, PermissionCode = PermissionCatalog.SelfRead, IsGranted = true });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "BASE1" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 2, NomBd = "BASE2" });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTG" });
        _uow.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 1, IdResponsable = 104, IdBdd = 1 });
    }

    /// <summary>Comptes variés : identifiant, rôle, lignes d'avant la migration (copies), retrait déjà au format écart.</summary>
    public static TheoryData<decimal, decimal, string[], bool> Accounts() => new()
    {
        { 101, 1, new[] { PermissionCatalog.SelfRead, "PARAMS:USERS" }, false },                       // admin personnalisé
        { 102, 1, Array.Empty<string>(), false },                                                       // admin sans ligne
        { 103, 10, Array.Empty<string>(), false },                                                      // hérité (porte seule)
        { 104, 10, new[] { "PROC:SHIFTS", "SAGE:BASE1:READ", "POINTEUSE:PTG:READ" }, false },           // copie réduite d'un rôle hérité
        { 105, 11, new[] { "PROC:SHIFTS", "PROC:SHIFTS:DELETE", "SAGE:BASE2:READ" }, false },          // ajouts et retraits
        { 106, 12, new[] { "OTHER:CANTEEN" }, false },                                                  // rôle vide
        { 107, 11, new[] { UserMarker }, false },                                                       // « aucun droit »
        { 108, 10, new[] { UserMarker }, false },                                                       // « aucun droit » sur rôle hérité
        { 109, 11, new[] { "PROC:SHIFTS", "SAGE:ANCIENNE", "POINTEUSE:VIEILLE" }, false },              // anciens codes sans action
        { 110, 11, new[] { "proc:shifts", " sage:base1:read " }, false },                               // casse et espaces
        { 111, 10, new[] { "OBSOLETE:CODE", "PARAMS:HOLIDAYS" }, false },                               // code inconnu
        { 112, 11, new[] { "PROC:SHIFTS:CREATE" }, true },                                              // déjà un retrait
        { 113, 13, Array.Empty<string>(), false },                                                      // rôle Salarié
        { 114, 999, new[] { "PROC:SHIFTS" }, false },                                                   // rôle introuvable
        { 115, 11, new[] { "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "SAGE:BASE1:READ" }, false },          // copie identique au rôle
        { 116, 13, new[] { PermissionCatalog.SelfRead, "REPORTS:REPORTS" }, false },                   // salarié + rapports
    };

    private void Seed(decimal id, decimal role, string[] codes, bool alreadyRemoval)
    {
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = id, IdRole = role, Login = $"u{id}" });
        foreach (var code in codes)
            _uow.Get<UserPermission>().Items.Add(new UserPermission
            {
                IdResponsable = id, PermissionCode = code, IsGranted = !alreadyRemoval, IsDeviation = false,
            });
    }

    [Theory]
    [MemberData(nameof(Accounts))]
    public async Task RunAsync_EveryAccount_EffectiveRightsAreIdentical(decimal id, decimal role, string[] codes, bool alreadyRemoval)
    {
        // Arrange
        Seed(id, role, codes, alreadyRemoval);
        var before = LegacyReading(id);

        // Act
        await new UserPermissionDeviationMigration(_uow, TestRights.Service(_uow), NullLogger<UserPermissionDeviationMigration>.Instance).RunAsync();
        var after = await TestRights.Service(_uow).GetAsync(id, fresh: true);

        // Assert
        _uow.Get<UserPermission>().Items.Should().NotContain(p => !p.IsDeviation, "aucune ancienne copie ne doit rester");
        after.Permissions.Should().BeEquivalentTo(before.Permissions);
        after.Gates.Should().BeEquivalentTo(before.Gates);
        after.IsAdministrator.Should().Be(before.IsAdministrator);
        after.RoleId.Should().Be(before.RoleId);
    }

    /// <summary>Ancienne lecture (avant la migration 006) : la copie complète est convertie en écarts à la volée.</summary>
    private EffectiveRights LegacyReading(decimal userId)
    {
        var user = _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == userId);
        var roleEntity = _uow.Get<Role>().Items.SingleOrDefault(r => r.IdRole == user.IdRole);
        var roleRows = roleEntity is null ? new List<PermissionRow>() : _uow.Get<RolePermission>().Items
            .Where(p => p.IdRole == roleEntity.IdRole).Select(p => new PermissionRow(p.PermissionCode, p.IsGranted)).ToList();
        var roleGates = roleEntity is null ? new List<int>() : _uow.Get<DroitRole>().Items
            .Where(p => p.IdRole == roleEntity.IdRole).Select(p => (int)p.IdDroit).ToList();
        var role = new RoleDefinition(roleEntity?.IdRole, roleEntity is not null, roleRows, roleGates);
        var sages = _uow.Get<BddSage>().Items;
        var access = _uow.Get<BddAutorisee>().Items.Where(a => a.IdResponsable == userId).Select(a => a.IdBdd).ToHashSet();
        var catalog = new DatabaseCatalog(
            sages.Select(s => s.NomBd ?? string.Empty).ToList(),
            _uow.Get<BddPointeuse>().Items.Select(p => p.NomBd ?? string.Empty).ToList(),
            sages.Where(s => access.Contains(s.Id)).Select(s => s.NomBd ?? string.Empty).ToList());
        var userRows = _uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == userId)
            .Select(p => new PermissionRow(p.PermissionCode, p.IsGranted)).ToList();

        // Copie à l'identique de l'ancien EffectiveRightsService.LegacyToDeviations.
        IReadOnlyCollection<PermissionRow> deviations = userRows;
        if (userRows.Count > 0 && userRows.All(r => r.IsGranted))
        {
            var roleOnly = EffectiveRightsCalculator.ComputeEffectiveRights(role, Array.Empty<PermissionRow>(), catalog);
            var (additions, removals) = EffectiveRightsCalculator.ConvertLegacyList(roleOnly.Permissions.ToList(), userRows.Select(r => r.Code).ToList());
            deviations = additions.Select(c => new PermissionRow(c, true)).Concat(removals.Select(c => new PermissionRow(c, false))).ToList();
        }
        return EffectiveRightsCalculator.ComputeEffectiveRights(role, deviations, catalog);
    }
}
