using AutoMapper;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Étape 4b-10 : les droits affichés d'un rôle sans lignes (et de ses comptes sans ajustement) sont les droits effectifs
/// du calculateur, donc sans la configuration des bases.
/// </summary>
public class DisplayedRightsTests
{
    private static readonly string[] Configuration =
    {
        "sage.create", "sage.all.update", "sage.all.delete",
        "pointeuse.create", "pointeuse.all.update", "pointeuse.all.delete",
    };

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AuthServices _auth;

    public DisplayedRightsTests()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), TestRights.Service(_uow), new PasswordHasher<Responsable>());

        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Sans lignes" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 6, NomRole = "Avec lignes" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "BASE1" });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTG" });
        // Rôle 5 : portes Bases + Traitements, aucune ligne. Rôle 6 : mêmes portes, lignes détaillées.
        foreach (var role in new decimal[] { 5, 6 })
        {
            _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = role, IdDroit = 1 });
            _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = role, IdDroit = 3 });
        }
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 6, PermissionCode = "sage.create", IsGranted = true });
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 6, PermissionCode = "shifts.read", IsGranted = true });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 15, IdRole = 5, Login = "sanslignes" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 16, IdRole = 6, Login = "aveclignes" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 17, IdRole = 5, Login = "ajustements" });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 17, PermissionCode = "sage.create", IsGranted = true });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 17, PermissionCode = "sage.1.read", IsGranted = true });
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_RoleWithGatesOnly_AfterMigration_ShowsReadButNoConfiguration()
    {
        // Arrange
        await MigrateAsync();

        // Act
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);

        // Assert
        role.IsConfigured.Should().BeTrue();
        role.GrantedPermissions.Should().Contain("sage.all.read");
        role.GrantedPermissions.Should().Contain("pointeuse.all.read");
        role.GrantedPermissions.Should().Contain("shifts.read");
        Configuration.Should().AllSatisfy(c => role.GrantedPermissions.Should().NotContain(c));
    }

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_AccountWithoutAdjustment_ShowsRoleRights()
    {
        // Arrange
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);

        // Act
        var user = await _auth.GetUserDetailedPermissionsAsync(15);

        // Assert
        user.IsCustomized.Should().BeFalse();
        role.GrantedPermissions.ToHashSet().SetEquals(user.GrantedPermissions).Should().BeTrue();
        Configuration.Should().AllSatisfy(c => user.GrantedPermissions.Should().NotContain(c));
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_RoleWithRows_IsUnchanged()
    {
        // Act
        var role = await _auth.GetRoleDetailedPermissionsAsync(6);

        // Assert
        role.IsConfigured.Should().BeTrue();
        role.GrantedPermissions.ToHashSet().SetEquals(new[] { "sage.create", "shifts.read" }).Should().BeTrue();
        var user = await _auth.GetUserDetailedPermissionsAsync(16);
        user.GrantedPermissions.ToHashSet().SetEquals(new[] { "sage.create", "shifts.read" }).Should().BeTrue();
    }

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_AccountWithPersonalRows_KeepsItsList()
    {
        // Act
        var user = await _auth.GetUserDetailedPermissionsAsync(17);

        // Assert
        user.IsCustomized.Should().BeTrue();
        user.GrantedPermissions.ToHashSet().SetEquals(new[] { "sage.create", "sage.1.read" }).Should().BeTrue();
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_Administrator_KeepsEveryRightIncludingConfiguration()
    {
        // Arrange
        var user = await _auth.GetUserDetailedPermissionsAsync(1);

        // Act
        var role = await _auth.GetRoleDetailedPermissionsAsync(1);

        // Assert
        Configuration.Should().AllSatisfy(c => user.GrantedPermissions.Should().Contain(c));
        Configuration.Should().AllSatisfy(c => role.GrantedPermissions.Should().Contain(c));
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_UnknownRole_ShowsNoRight()
    {
        // Act
        var role = await _auth.GetRoleDetailedPermissionsAsync(999);

        // Assert
        role.IsConfigured.Should().BeFalse();
        role.GrantedPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_DatabaseGateWithoutDatabase_KeepsAllDatabasesReadJoker()
    {
        // Arrange
        // Sans aucune base déclarée, la porte Bases devient quand même la lecture de toutes les bases (joker) :
        // une base ajoutée plus tard sera lisible, et réenregistrer ces droits conserve la porte Bases.
        _uow.Get<BddSage>().Items.Clear();
        _uow.Get<BddPointeuse>().Items.Clear();
        await MigrateAsync();
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);
        await _auth.SaveRoleDetailedPermissionsAsync(1, 5, role.GrantedPermissions);

        // Act
        var gates = _uow.Get<DroitRole>().Items.Where(d => d.IdRole == 5).Select(d => (int)d.IdDroit).OrderBy(x => x);

        // Assert
        gates.Should().Equal(new[] { 1, 3 });
    }

    private Task MigrateAsync()
        => new RightsStorageMigration(_uow, NullLogger<RightsStorageMigration>.Instance).RunAsync();

    private sealed class Token : ITokenService
    {
        public string CreateToken(TokenSubject subject) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
