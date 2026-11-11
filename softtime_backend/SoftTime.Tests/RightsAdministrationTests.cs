using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

using AutoMapper;

namespace SoftTime.Tests;

/// <summary>
/// Lot 2 : même validation des codes pour un compte et un rôle, journal sur toutes les modifications de droits,
/// suppression d'un rôle utilisé refusée, catalogue unique exposé à l'écran.
/// </summary>
public class RightsAdministrationTests
{
    private const decimal AdminId = 1;
    private const decimal UserId = 2;
    private const decimal UsedRoleId = 2;
    private const decimal FreeRoleId = 3;

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AuthServices _auth;

    public RightsAdministrationTests()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(
            _uow, ServiceMocks.TokenService().Object, mapper, ServiceMocks.EmailService().Object,
            new Http.AdminCurrentUser(), Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());

        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = UsedRoleId, NomRole = "Utilisateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = FreeRoleId, NomRole = "Libre" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = AdminId, IdRole = 1, Login = "admin", NomResponsable = "Admin" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = UserId, IdRole = UsedRoleId, Login = "user", NomResponsable = "User" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "PAIE" });
    }

    private static string Resource(string pageCode) => pageCode[..pageCode.LastIndexOf('.')];

    private List<AuditDroit> Journal(string action) => _uow.Get<AuditDroit>().Items.Where(a => a.Action == action).ToList();

    [Theory]
    [InlineData("INVALID:CODE")]
    [InlineData("sage.PAIE.write")]
    [InlineData("SAGE:PAIE:READ")]
    public async Task SaveUserDetailedPermissionsAsync_UnknownOrMalformedCode_ThrowsDomainException(string code)
    {
        // Act
        Func<Task> act = () => _auth.SaveUserDetailedPermissionsAsync(AdminId, UserId, new[] { code });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Contain("Codes de permission inconnus");
        _uow.Get<UserPermission>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_DuplicatesWithSpaces_SavedOnceNormalized()
    {
        // Act
        await _auth.SaveUserDetailedPermissionsAsync(AdminId, UserId, new[] { " SHIFTS.READ", "shifts.read ", "Self.Read" });

        // Assert
        _uow.Get<UserPermission>().Items.Select(p => p.PermissionCode)
            .Should().BeEquivalentTo("shifts.read", PermissionCatalog.SelfRead);
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_FullList_IsStoredAsDeviationsFromTheRole()
    {
        // Arrange
        foreach (var code in new[] { "shifts.read", "reports.read" })
            _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = UsedRoleId, PermissionCode = code, IsGranted = true });

        // Act
        await _auth.SaveUserDetailedPermissionsAsync(AdminId, UserId, new[] { "shifts.read", "canteen.manage" });

        // Assert
        _uow.Get<UserPermission>().Items.Select(p => (p.PermissionCode, p.IsGranted))
            .Should().BeEquivalentTo(new[] { ("canteen.manage", true), ("reports.read", false) });
        var shown = await _auth.GetUserDetailedPermissionsAsync(UserId);
        shown.GrantedPermissions.Should().BeEquivalentTo("shifts.read", "canteen.manage");
        shown.IsCustomized.Should().BeTrue();
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_ListEqualToRole_StoresNothing()
    {
        // Arrange
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = UsedRoleId, PermissionCode = "shifts.read", IsGranted = true });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = UserId, PermissionCode = "canteen.manage", IsGranted = true });

        // Act
        await _auth.SaveUserDetailedPermissionsAsync(AdminId, UserId, new[] { "shifts.read" });

        // Assert
        _uow.Get<UserPermission>().Items.Should().BeEmpty();
        (await _auth.GetUserDetailedPermissionsAsync(UserId)).IsCustomized.Should().BeFalse();
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_AdministratorTarget_IsRefused()
    {
        // Act
        Func<Task> act = () => _auth.SaveUserDetailedPermissionsAsync(AdminId, AdminId, new[] { "shifts.read" });

        // Assert
        await act.Should().ThrowExactlyAsync<DomainException>();
        _uow.Get<UserPermission>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_SelfRead_IsAccepted()
    {
        // Act
        await _auth.SaveRoleDetailedPermissionsAsync(AdminId, FreeRoleId, new[] { PermissionCatalog.SelfRead });

        // Assert
        _uow.Get<RolePermission>().Items.Should().ContainSingle(p => p.PermissionCode == PermissionCatalog.SelfRead);
        _uow.Get<DroitRole>().Items.Where(d => d.IdRole == FreeRoleId).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRoleAsync_RoleStillAssigned_ThrowsConflictAndDeletesNothing()
    {
        // Act
        Func<Task> act = () => _auth.DeleteRoleAsync(UsedRoleId);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<ConflictException>()).Which;
        ex.Message.Should().Contain("1 utilisateur");
        _uow.Get<Role>().Items.Should().Contain(r => r.IdRole == UsedRoleId);
        Journal(RightsChangeActions.DeleteRole).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRoleAsync_UnassignedRole_IsDeletedAndJournaled()
    {
        // Arrange
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = FreeRoleId, PermissionCode = "shifts.read", IsGranted = true });

        // Act
        await _auth.DeleteRoleAsync(FreeRoleId);

        // Assert
        _uow.Get<Role>().Items.Should().NotContain(r => r.IdRole == FreeRoleId);
        var entry = Journal(RightsChangeActions.DeleteRole).Should().ContainSingle().Which;
        entry.IdCible.Should().Be(FreeRoleId);
        entry.NomCible.Should().Be("Libre");
        entry.PermsAvant.Should().Be("shifts.read");
    }

    [Fact]
    public async Task SaveRoleAsync_CreateAndRename_AreJournaled()
    {
        // Act
        await _auth.SaveRoleAsync(new RoleDto(0, "Nouveau"));
        await _auth.SaveRoleAsync(new RoleDto(FreeRoleId, "Renommé"));

        // Assert
        Journal(RightsChangeActions.CreateRole).Should().ContainSingle(a => a.NomCible == "Nouveau");
        var rename = Journal(RightsChangeActions.RenameRole).Should().ContainSingle().Which;
        rename.PermsAvant.Should().Be("Libre");
        rename.PermsApres.Should().Be("Renommé");
        rename.LoginOperateur.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SetDbAccessAsync_Bases_AreJournaledByName()
    {
        // Act
        await _auth.SetDbAccessAsync(UserId, new[] { 7 });

        // Assert
        var entry = Journal(RightsChangeActions.UpdateDatabaseAccess).Should().ContainSingle().Which;
        entry.IdCible.Should().Be(UserId);
        entry.PermsApres.Should().Be("PAIE");
    }

    [Fact]
    public async Task SetDbAccessAsync_UnknownBase_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () => _auth.SetDbAccessAsync(UserId, new[] { 999 });

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("999");
        _uow.Get<BddAutorisee>().Items.Should().BeEmpty();
    }

    [Fact]
    public void GetCatalog_EveryCode_IsAcceptedByTheValidation()
    {
        // Act
        var codes = _auth.Privileges.GetCatalog()
            .SelectMany(c => c.Items)
            .SelectMany(i => new[]
            {
                i.Code,
                i.CanCreate ? Resource(i.Code) + ".create" : null,
                i.CanUpdate ? Resource(i.Code) + ".update" : null,
                i.CanDelete ? Resource(i.Code) + ".delete" : null
            })
            .Where(code => code is not null)
            .ToList();

        // Assert
        PermissionCodeNormalizer.Normalize(codes).Should().HaveCount(codes.Count);
        codes.Should().Contain(PermissionCatalog.SelfRead);
    }
}
