using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
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
        "SAGE:CREATE", "SAGE:BASE1:UPDATE", "SAGE:BASE1:DELETE",
        "POINTEUSE:CREATE", "POINTEUSE:PTG:UPDATE", "POINTEUSE:PTG:DELETE",
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
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 6, PermissionCode = "SAGE:CREATE", IsGranted = true });
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 6, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 15, IdRole = 5, Login = "sanslignes" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 16, IdRole = 6, Login = "aveclignes" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 17, IdRole = 5, Login = "ajustements" });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 17, PermissionCode = "SAGE:CREATE", IsGranted = true });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 17, PermissionCode = "SAGE:BASE1:READ", IsGranted = true });
    }

    [Fact]
    public async Task Role_without_rows_shows_database_read_but_no_configuration()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);
        role.IsConfigured.Should().BeFalse();
        role.GrantedPermissions.Should().Contain("SAGE:BASE1:READ");
        role.GrantedPermissions.Should().Contain("POINTEUSE:PTG:READ");
        role.GrantedPermissions.Should().Contain("PROC:SHIFTS");
        Configuration.Should().AllSatisfy(c => role.GrantedPermissions.Should().NotContain(c));
    }

    [Fact]
    public async Task Account_of_that_role_without_adjustment_shows_the_same_rights()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);
        var user = await _auth.GetUserDetailedPermissionsAsync(15);
        user.IsCustomized.Should().BeFalse();
        role.GrantedPermissions.ToHashSet().SetEquals(user.GrantedPermissions).Should().BeTrue();
        Configuration.Should().AllSatisfy(c => user.GrantedPermissions.Should().NotContain(c));
    }

    [Fact]
    public async Task Role_with_rows_is_unchanged()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(6);
        role.IsConfigured.Should().BeTrue();
        role.GrantedPermissions.ToHashSet().SetEquals(new[] { "SAGE:CREATE", "PROC:SHIFTS" }).Should().BeTrue();
        var user = await _auth.GetUserDetailedPermissionsAsync(16);
        user.GrantedPermissions.ToHashSet().SetEquals(new[] { "SAGE:CREATE", "PROC:SHIFTS" }).Should().BeTrue();
    }

    [Fact]
    public async Task Account_with_personal_rows_keeps_its_list()
    {
        var user = await _auth.GetUserDetailedPermissionsAsync(17);
        user.IsCustomized.Should().BeTrue();
        user.GrantedPermissions.ToHashSet().SetEquals(new[] { "SAGE:CREATE", "SAGE:BASE1:READ" }).Should().BeTrue();
    }

    [Fact]
    public async Task Administrator_keeps_every_right_including_configuration()
    {
        var user = await _auth.GetUserDetailedPermissionsAsync(1);
        var role = await _auth.GetRoleDetailedPermissionsAsync(1);
        Configuration.Should().AllSatisfy(c => user.GrantedPermissions.Should().Contain(c));
        Configuration.Should().AllSatisfy(c => role.GrantedPermissions.Should().Contain(c));
    }

    [Fact]
    public async Task Unknown_role_shows_no_right()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(999);
        role.IsConfigured.Should().BeFalse();
        role.GrantedPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Database_gate_without_any_database_is_not_carried_by_the_displayed_rights()
    {
        // Cas limite documenté : sans base, aucun droit SAGE/POINTEUSE n'est affiché, donc réenregistrer ces droits
        // ne conserve pas la porte Bases (elle ne donnerait de toute façon accès à rien).
        _uow.Get<BddSage>().Items.Clear();
        _uow.Get<BddPointeuse>().Items.Clear();
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);
        await _auth.SaveRoleDetailedPermissionsAsync(1, 5, role.GrantedPermissions);
        var gates = _uow.Get<DroitRole>().Items.Where(d => d.IdRole == 5).Select(d => (int)d.IdDroit).OrderBy(x => x);
        gates.Should().Equal(new[] { 3 });
    }

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
