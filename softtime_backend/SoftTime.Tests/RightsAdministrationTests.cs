using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Domain;
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

    private List<AuditDroit> Journal(string action) => _uow.Get<AuditDroit>().Items.Where(a => a.Action == action).ToList();

    [Theory]
    [InlineData("INVALID:CODE")]
    [InlineData("SAGE:PAIE:WRITE")]
    [InlineData("SAGE:A:B:READ")]
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
        await _auth.SaveUserDetailedPermissionsAsync(AdminId, UserId, new[] { " proc:shifts", "PROC:SHIFTS ", "self:read" });

        // Assert
        _uow.Get<UserPermission>().Items.Select(p => p.PermissionCode)
            .Should().BeEquivalentTo("PROC:SHIFTS", PermissionCatalog.SelfRead);
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
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = FreeRoleId, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        // Act
        await _auth.DeleteRoleAsync(FreeRoleId);

        // Assert
        _uow.Get<Role>().Items.Should().NotContain(r => r.IdRole == FreeRoleId);
        var entry = Journal(RightsChangeActions.DeleteRole).Should().ContainSingle().Which;
        entry.IdCible.Should().Be(FreeRoleId);
        entry.NomCible.Should().Be("Libre");
        entry.PermsAvant.Should().Be("PROC:SHIFTS");
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
    public async Task SetPrivilegesAsync_Gates_AreJournaled()
    {
        // Act
        await _auth.SetPrivilegesAsync(FreeRoleId, new List<decimal> { Rights.Reports, Rights.Processing });

        // Assert
        var entry = Journal(RightsChangeActions.UpdateRoleGates).Should().ContainSingle().Which;
        entry.PermsAvant.Should().BeEmpty();
        entry.PermsApres.Should().Be("GATE:3,GATE:6");
    }

    [Fact]
    public async Task SetPrivilegesAsync_UnknownGate_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () => _auth.SetPrivilegesAsync(FreeRoleId, new List<decimal> { 42 });

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("42");
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
                i.CanCreate ? i.Code + ":CREATE" : null,
                i.CanUpdate ? i.Code + ":UPDATE" : null,
                i.CanDelete ? i.Code + ":DELETE" : null
            })
            .Where(code => code is not null)
            .ToList();

        // Assert
        PermissionCodeNormalizer.Normalize(codes).Should().HaveCount(codes.Count);
        codes.Should().Contain(PermissionCatalog.SelfRead);
    }
}
