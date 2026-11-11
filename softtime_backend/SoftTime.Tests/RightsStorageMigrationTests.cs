using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Lot 3 : migration DROITS_ECARTS_V1. Les droits effectifs de chaque compte sont les mêmes avant et après, la migration
/// ne se rejoue pas, et un compte converti suit désormais les changements de son rôle.
/// </summary>
public class RightsStorageMigrationTests
{
    private const decimal GateRoleId = 20;
    private const decimal DetailedRoleId = 21;

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly RightsStorageMigration _sut;

    public RightsStorageMigrationTests()
    {
        _sut = new RightsStorageMigration(_uow, NullLogger<RightsStorageMigration>.Instance);
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "PAIE" });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTG" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = GateRoleId, NomRole = "Portes seules" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = DetailedRoleId, NomRole = "Détaillé" });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = GateRoleId, IdDroit = Rights.Databases });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = GateRoleId, IdDroit = Rights.Processing });
        foreach (var code in new[] { "shifts.read", "reports.read" })
            _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = DetailedRoleId, PermissionCode = code, IsGranted = true });
    }

    private void Account(decimal id, decimal role, params string[] legacyList)
    {
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = id, IdRole = role, Login = $"u{id}" });
        foreach (var code in legacyList)
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = id, PermissionCode = code, IsGranted = true });
    }

    private HashSet<string> UserRows(decimal id, bool granted)
        => _uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == id && p.IsGranted == granted)
            .Select(p => p.PermissionCode).ToHashSet();

    private async Task<HashSet<string>> EffectiveAsync(decimal id)
        => (await TestRights.Service(_uow).GetAsync(id, fresh: true)).Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task RunAsync_GateOnlyRole_ReceivesTheRightsItsGatesGave()
    {
        // Act
        var result = await _sut.RunAsync();

        // Assert
        result.ConvertedRoles.Should().Be(1);
        var rows = _uow.Get<RolePermission>().Items.Where(p => p.IdRole == GateRoleId).Select(p => p.PermissionCode).ToList();
        rows.Should().Contain(new[] { "shifts.read", "sage.all.read", "pointeuse.all.read" });
        rows.Should().NotContain(new[] { "sage.create", "sage.all.update", "sage.1.read", "users.read" });
        _uow.Get<RolePermission>().Items.Where(p => p.IdRole == 1).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_LegacyList_BecomesDeviationsWithSameEffectiveRights()
    {
        // Arrange : liste complète = droits du rôle détaillé, moins reports.read, plus users.read.
        Account(100, DetailedRoleId, "shifts.read", "users.read");

        // Act
        await _sut.RunAsync();

        // Assert
        UserRows(100, granted: true).Should().BeEquivalentTo("users.read");
        UserRows(100, granted: false).Should().BeEquivalentTo("reports.read");
        (await EffectiveAsync(100)).Should().BeEquivalentTo("shifts.read", "users.read");
    }

    [Fact]
    public async Task RunAsync_ListEqualToRole_LeavesNoDeviation()
    {
        // Arrange
        Account(101, DetailedRoleId, "shifts.read", "reports.read");

        // Act
        await _sut.RunAsync();

        // Assert
        _uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == 101).Should().BeEmpty();
        (await TestRights.Service(_uow).GetAsync(101, fresh: true)).IsCustomized.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_NoRightMarker_IsKept()
    {
        // Arrange
        Account(102, DetailedRoleId, PermissionCatalog.EmptyUserMarker);

        // Act
        await _sut.RunAsync();

        // Assert
        UserRows(102, granted: true).Should().BeEquivalentTo(PermissionCatalog.EmptyUserMarker);
        (await EffectiveAsync(102)).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SecondRun_DoesNothing()
    {
        // Arrange
        Account(103, DetailedRoleId, "shifts.read");
        await _sut.RunAsync();
        var afterFirst = _uow.Snapshot();

        // Act
        var second = await _sut.RunAsync();

        // Assert
        second.AlreadyApplied.Should().BeTrue();
        _uow.Snapshot().Should().Be(afterFirst);
    }

    [Fact]
    public async Task ConvertedAccount_RoleGainsARight_AccountGainsItToo()
    {
        // Arrange : avant, une liste complète figeait le compte ; en écarts, il suit son rôle.
        Account(104, DetailedRoleId, "shifts.read", "users.read");
        await _sut.RunAsync();
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = DetailedRoleId, PermissionCode = "canteen.manage", IsGranted = true });

        // Act
        var effective = await EffectiveAsync(104);

        // Assert
        effective.Should().Contain("canteen.manage");
        effective.Should().NotContain("reports.read");
    }
}
