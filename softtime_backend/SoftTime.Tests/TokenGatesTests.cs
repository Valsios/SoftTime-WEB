using System.IdentityModel.Tokens.Jwt;

using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Api.Security;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Étape 4b-7 : les portes du claim « rights » viennent des droits effectifs (rôle + ajouts − retraits).
/// Jetons produits par le vrai JwtTokenService avec une clé de test, comptes et mots de passe factices.
/// </summary>
public class TokenGatesTests
{
    private const string FakePassword = "FAKE-LOGIN-PWD";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AuthServices _auth;

    public TokenGatesTests()
    {
        var jwt = Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Key = "test-only-signing-key-0123456789-abcdefghijklmnop",
            Issuer = "SoftTime.Tests",
            Audience = "SoftTime.Tests.Web",
        });
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(_uow, new JwtTokenService(jwt), mapper, new Mail(), new AdminCurrentUser(), TestRights.Service(_uow), new PasswordHasher<Responsable>());

        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 29, NomRole = "Consultation" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 10034, NomRole = "Utilisateur ordinaire" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 3033, NomBd = "BASE1" });

        // Consultation : lignes détaillées « Rapports » seulement.
        Role(29, new[] { "REPORTS:REPORTS" }, 6);
        // Utilisateur ordinaire : portes 1 et 3 avec lignes détaillées.
        Role(10034, new[] { "SAGE:BASE1:READ", "PROC:SHIFTS" }, 1, 3);
    }

    private void Role(decimal id, string[] codes, params int[] gates)
    {
        foreach (var c in codes) _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = id, PermissionCode = c, IsGranted = true });
        foreach (var g in gates) _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = id, IdDroit = g });
    }

    private void Account(decimal id, decimal role, string login, params string[] userRows)
    {
        var user = new Responsable { IdResponsable = id, IdRole = role, Login = login };
        user.Password = new PasswordHasher<Responsable>().HashPassword(user, FakePassword);
        _uow.Get<Responsable>().Items.Add(user);
        foreach (var c in userRows) _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = id, PermissionCode = c, IsGranted = true });
    }

    private async Task<(int[] TokenGates, int[] ResponseGates, LoginResponse Login)> GatesOf(string login, decimal id)
    {
        var response = await _auth.LoginAsync(new LoginRequest(login, FakePassword));
        var claim = new JwtSecurityTokenHandler().ReadJwtToken(response.Token).Claims.First(c => c.Type == "rights").Value;
        var token = claim.Length == 0 ? Array.Empty<int>() : claim.Split(',').Select(int.Parse).OrderBy(x => x).ToArray();
        var me = await _auth.GetMeAsync(id);
        me.Rights.OrderBy(x => x).Should().Equal(response.Rights.OrderBy(x => x));       // /auth/me renvoie les mêmes portes
        return (token, response.Rights.OrderBy(x => x).ToArray(), response);
    }

    [Fact]
    public async Task Administrator_has_gates_1_to_6()
    {
        // Arrange
        Account(14, 1, "admin");

        // Act
        var (token, response, _) = await GatesOf("admin", 14);

        // Assert
        token.Should().Equal(new[] { 1, 2, 3, 4, 5, 6 });
        response.Should().Equal(token);
    }

    [Fact]
    public async Task Account_with_the_no_right_marker_has_no_gate()
    {
        // Arrange
        Account(10028, 10034, "user", PermissionCatalogMarker);

        // Act
        var (token, response, login) = await GatesOf("user", 10028);

        // Assert
        token.Should().BeEmpty();
        response.Should().BeEmpty();
        login.AuthorizedDatabases.Should().BeEmpty();
    }

    [Fact]
    public async Task Consultation_account_has_gate_6_only()
    {
        // Act
        Account(10030, 29, "user1");

        // Assert
        (await GatesOf("user1", 10030)).TokenGates.Should().Equal(new[] { 6 });
    }

    [Fact]
    public async Task Account_like_joel_with_role_only_has_gates_1_and_3_and_keeps_its_databases()
    {
        // Arrange
        Account(10034, 10034, "joel");
        _uow.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 1, IdResponsable = 10034, IdBdd = 3033 });

        // Act
        var (token, _, login) = await GatesOf("joel", 10034);

        // Assert
        token.Should().Equal(new[] { 1, 3 });
        login.AuthorizedDatabases.Should().ContainSingle().Which.NomBd.Should().Be("BASE1");
    }

    [Fact]
    public async Task Account_like_joel_with_PARAMS_additions_gets_gate_2_too()
    {
        // Arrange
        // Stockage actuel : liste complète = droits du rôle + ajouts (convertie en écarts à la lecture).
        Account(10034, 10034, "joel", "SAGE:BASE1:READ", "PROC:SHIFTS", "PARAMS:USERS", "PARAMS:PRIVILEGES");

        // Act
        var (token, _, _) = await GatesOf("joel", 10034);

        // Assert
        token.Should().Equal(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Removing_every_work_right_removes_gate_3()
    {
        // Act
        Account(10034, 10034, "joel", "SAGE:BASE1:READ");

        // Assert
        (await GatesOf("joel", 10034)).TokenGates.Should().Equal(new[] { 1 });
    }

    [Fact]
    public async Task Account_whose_role_does_not_exist_has_no_gate()
    {
        // Arrange
        Account(10099, 999, "orphan");

        // Act
        var (token, response, login) = await GatesOf("orphan", 10099);

        // Assert
        token.Should().BeEmpty();
        response.Should().BeEmpty();
        login.AuthorizedDatabases.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rights_change_is_visible_at_the_next_login()
    {
        // Arrange
        Account(14, 1, "admin");

        // Act
        Account(10034, 10034, "joel");

        // Assert
        (await GatesOf("joel", 10034)).TokenGates.Should().Equal(new[] { 1, 3 });
        await _auth.SaveUserDetailedPermissionsAsync(14, 10034, new[] { "SAGE:BASE1:READ", "PROC:SHIFTS", "PARAMS:USERS" });
        (await GatesOf("joel", 10034)).TokenGates.Should().Equal(new[] { 1, 2, 3 });
    }

    // S2 : PUT privileges/{role} ne doit pas contredire les portes déduites des droits détaillés d'un rôle.
    [Fact]
    public async Task SetPrivileges_is_refused_for_a_role_with_detailed_rights_and_still_works_without()
    {
        // Act
        Func<Task> act = () => _auth.SetPrivilegesAsync(10034, new List<decimal> { 6 });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Contain("droits détaillés");
        _uow.Get<DroitRole>().Items.Where(d => d.IdRole == 10034).Select(d => d.IdDroit).OrderBy(x => x).Should().Equal(new[] { 1m, 3m });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 50, NomRole = "Sans lignes" });
        await _auth.SetPrivilegesAsync(50, new List<decimal> { 6 });
        _uow.Get<DroitRole>().Items.Where(d => d.IdRole == 50).Should().ContainSingle();
    }

    private const string PermissionCatalogMarker = "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__";

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
