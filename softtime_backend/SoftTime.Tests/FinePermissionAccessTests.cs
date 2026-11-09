using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Contrôle serveur d'une permission fine (porte ET permission dans les droits effectifs).</summary>
public class FinePermissionAccessTests
{
    private static readonly FinePermission ShiftDelete = new(3, "PROC:SHIFTS:DELETE");

    private readonly InMemoryUnitOfWork _uow = new();

    public FinePermissionAccessTests()
    {
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Sans droit" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Traitements" });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = 3 });
    }

    private Task Ensure(decimal userId, decimal roleId, params string[] userCodes)
    {
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = userId, IdRole = roleId, Login = $"u{userId}" });
        foreach (var code in userCodes)
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = userId, PermissionCode = code, IsGranted = true });
        return FinePermissionAccess.EnsureAsync(
            new TestCurrentUser(userId, roleId, 3), TestRights.Service(_uow), ShiftDelete, FinePermissionAccess.ActionDenied);
    }

    [Fact]
    public async Task EnsureAsync_Administrator_Passes()
    {
        // Act
        var act = () => Ensure(1, 1);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureAsync_GateAndFinePermission_Passes()
    {
        // Act
        var act = () => Ensure(10, 2, "PROC:SHIFTS", "PROC:SHIFTS:DELETE");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureAsync_GateWithoutFinePermission_ThrowsForbidden()
    {
        // Act
        var act = () => Ensure(11, 2, "PROC:SHIFTS");

        // Assert
        (await act.Should().ThrowExactlyAsync<ForbiddenException>()).Which.Message.Should().Be(FinePermissionAccess.ActionDenied);
    }

    [Fact]
    public async Task EnsureAsync_LegacyAccountWithoutAnyFinePermission_FallsBackToGate()
    {
        // Act
        // Rôle avec la porte 3, aucune ligne de permission ni sur le rôle ni sur le compte.
        var act = () => Ensure(12, 3);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureAsync_RoleRowsWithoutPermission_ThrowsForbidden()
    {
        // Arrange
        // Le rôle a des lignes détaillées : la règle héritée (repli sur la porte) ne s'applique plus.
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        // Act
        var act = () => Ensure(13, 3);

        // Assert
        await act.Should().ThrowExactlyAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureAsync_UnknownAccount_ThrowsForbidden()
    {
        // Act
        var act = () => FinePermissionAccess.EnsureAsync(
            new TestCurrentUser(999, 2, 3), TestRights.Service(_uow), ShiftDelete, FinePermissionAccess.ActionDenied);

        // Assert
        await act.Should().ThrowExactlyAsync<ForbiddenException>();
    }
}
