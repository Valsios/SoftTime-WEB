using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.Users;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Droits d'un compte avec leur source (rôle, ajouté, retiré) et retrait des anciens codes sans action.</summary>
public class UserRightsDescriberTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AuthServices _auth;
    private readonly UserRightsDescriber _sut;

    public UserRightsDescriberTests()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(_uow, ServiceMocks.TokenService().Object, mapper, ServiceMocks.EmailService().Object,
            new AdminCurrentUser(), TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _sut = new UserRightsDescriber(_auth, TestRights.Service(_uow), _uow);

        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Gestionnaire" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
    }

    private static EffectivePermissionDto Right(UserDetailedPermissionsDto dto, string code) => dto.Rights.Single(r => r.Code == code);

    [Fact]
    public async Task DescribeAsync_CustomizedAccount_GivesTheSourceOfEachRight()
    {
        // Arrange
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PROC:SHIFTS:CREATE" });
        await _auth.SaveUserDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PARAMS:USERS" });

        // Act
        var result = await _sut.DescribeAsync(3);

        // Assert
        Right(result, "PROC:SHIFTS").Should().Be(new EffectivePermissionDto("PROC:SHIFTS", PermissionSources.Role, true, false));
        Right(result, "PARAMS:USERS").Should().Be(new EffectivePermissionDto("PARAMS:USERS", PermissionSources.Added, true, false));
        Right(result, "PROC:SHIFTS:CREATE").Should().Be(new EffectivePermissionDto("PROC:SHIFTS:CREATE", PermissionSources.Removed, false, false));
        result.GrantedPermissions.Should().BeEquivalentTo("PROC:SHIFTS", "PARAMS:USERS");
    }

    [Fact]
    public async Task DescribeAsync_LegacyCodeWithoutAction_IsListedAsLegacyButHiddenFromTheScreenList()
    {
        // Arrange
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "SAGE:ANCIENNE", IsGranted = true });

        // Act
        var result = await _sut.DescribeAsync(3);

        // Assert
        Right(result, "SAGE:ANCIENNE").Should().Be(new EffectivePermissionDto("SAGE:ANCIENNE", PermissionSources.Added, true, true));
        result.GrantedPermissions.Should().NotContain("SAGE:ANCIENNE");
    }

    [Fact]
    public async Task DescribeAsync_AdministratorAddition_IsListedAsNotEffective()
    {
        // Arrange
        // Décision 1 (admin et SELF:READ) hors périmètre : le calcul ignore encore les lignes de l'administrateur.
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 1, PermissionCode = PermissionCatalog.SelfRead, IsGranted = true });

        // Act
        var result = await _sut.DescribeAsync(1);

        // Assert
        Right(result, PermissionCatalog.SelfRead).Should().Be(new EffectivePermissionDto(PermissionCatalog.SelfRead, PermissionSources.Added, false, false));
        Right(result, "PARAMS:PRIVILEGES").Source.Should().Be(PermissionSources.Role);
    }

    [Fact]
    public async Task RemoveLegacyPermissionsAsync_ConfirmedCode_IsRemovedAndJournaled()
    {
        // Arrange
        foreach (var code in new[] { "PROC:SHIFTS", "SAGE:ANCIENNE" })
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = code, IsGranted = true });

        // Act
        await _auth.RemoveLegacyPermissionsAsync(1, 3, new[] { "sage:ancienne" });

        // Assert
        _uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == 3).Select(p => p.PermissionCode).Should().Equal("PROC:SHIFTS");
        var audit = _uow.Get<AuditDroit>().Items.Should().ContainSingle(a => a.Action == "REMOVE_LEGACY_PERMS").Which;
        audit.PermsAvant.Should().Be("SAGE:ANCIENNE");
        audit.IdCible.Should().Be(3);
        audit.LoginOperateur.Should().Be("admin");
    }

    [Theory]
    [InlineData("PROC:SHIFTS")]
    [InlineData("SAGE:INCONNUE")]
    public async Task RemoveLegacyPermissionsAsync_CodeThatIsNotAStoredLegacyCode_IsRefused(string code)
    {
        // Arrange
        foreach (var stored in new[] { "PROC:SHIFTS", "SAGE:ANCIENNE" })
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = stored, IsGranted = true });
        var before = _uow.Snapshot();

        // Act
        Func<Task> act = () => _auth.RemoveLegacyPermissionsAsync(1, 3, new[] { code });

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().StartWith("Ancien code introuvable pour ce compte");
        _uow.Snapshot().Should().Be(before);
    }
}
