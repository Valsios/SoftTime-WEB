using System.IdentityModel.Tokens.Jwt;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Api.Security;
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

    private readonly SnapshotInMemoryUnitOfWork _uow = new();
    private readonly AuthService _auth;

    public TokenGatesTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-only-signing-key-0123456789-abcdefghijklmnop",
            ["Jwt:Issuer"] = "SoftTime.Tests",
            ["Jwt:Audience"] = "SoftTime.Tests.Web",
        }).Build();
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthService(_uow, new JwtTokenService(config), mapper, new Mail(), new AdminCurrentUser(), TestRights.Service(_uow), new PasswordHasher<T_RESPONSABLE>());

        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 29, NOMROLE = "Consultation" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 10034, NOMROLE = "Utilisateur ordinaire" });
        _uow.GetRepo<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 3033, NOM_BD = "BASE1" });

        // Consultation : lignes détaillées « Rapports » seulement.
        Role(29, new[] { "REPORTS:REPORTS" }, 6);
        // Utilisateur ordinaire : portes 1 et 3 avec lignes détaillées.
        Role(10034, new[] { "SAGE:BASE1:READ", "PROC:SHIFTS" }, 1, 3);
    }

    private void Role(decimal id, string[] codes, params int[] gates)
    {
        foreach (var c in codes) _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = id, PERMISSION_CODE = c, IS_GRANTED = true });
        foreach (var g in gates) _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = id, IDDROIT = g });
    }

    private void Account(decimal id, decimal role, string login, params string[] userRows)
    {
        var user = new T_RESPONSABLE { IDRESPONSABLE = id, IDROLE = role, LOGIN = login };
        user.PASSWORD = new PasswordHasher<T_RESPONSABLE>().HashPassword(user, FakePassword);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);
        foreach (var c in userRows) _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = id, PERMISSION_CODE = c, IS_GRANTED = true });
    }

    private async Task<(int[] TokenGates, int[] ResponseGates, LoginResponse Login)> GatesOf(string login, decimal id)
    {
        var response = await _auth.LoginAsync(new LoginRequest(login, FakePassword));
        var claim = new JwtSecurityTokenHandler().ReadJwtToken(response.Token).Claims.First(c => c.Type == "rights").Value;
        var token = claim.Length == 0 ? Array.Empty<int>() : claim.Split(',').Select(int.Parse).OrderBy(x => x).ToArray();
        var me = await _auth.GetMeAsync(id);
        Assert.Equal(response.Rights.OrderBy(x => x), me.Rights.OrderBy(x => x));       // /auth/me renvoie les mêmes portes
        return (token, response.Rights.OrderBy(x => x).ToArray(), response);
    }

    [Fact]
    public async Task Administrator_has_gates_1_to_6()
    {
        Account(14, 1, "admin");
        var (token, response, _) = await GatesOf("admin", 14);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, token);
        Assert.Equal(token, response);
    }

    [Fact]
    public async Task Account_with_the_no_right_marker_has_no_gate()
    {
        Account(10028, 10034, "user", PermissionCatalogMarker);
        var (token, response, login) = await GatesOf("user", 10028);
        Assert.Empty(token);
        Assert.Empty(response);
        Assert.Empty(login.AuthorizedDatabases);
    }

    [Fact]
    public async Task Consultation_account_has_gate_6_only()
    {
        Account(10030, 29, "user1");
        Assert.Equal(new[] { 6 }, (await GatesOf("user1", 10030)).TokenGates);
    }

    [Fact]
    public async Task Account_like_joel_with_role_only_has_gates_1_and_3_and_keeps_its_databases()
    {
        Account(10034, 10034, "joel");
        _uow.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 1, IDRESPONSABLE = 10034, IDBDD = 3033 });
        var (token, _, login) = await GatesOf("joel", 10034);
        Assert.Equal(new[] { 1, 3 }, token);
        Assert.Equal("BASE1", Assert.Single(login.AuthorizedDatabases).NomBd);   // bases proposées : règle de l'étape 4b-5
    }

    [Fact]
    public async Task Account_like_joel_with_PARAMS_additions_gets_gate_2_too()
    {
        // Stockage actuel : liste complète = droits du rôle + ajouts (convertie en écarts à la lecture).
        Account(10034, 10034, "joel", "SAGE:BASE1:READ", "PROC:SHIFTS", "PARAMS:USERS", "PARAMS:PRIVILEGES");
        var (token, _, _) = await GatesOf("joel", 10034);
        Assert.Equal(new[] { 1, 2, 3 }, token);
    }

    [Fact]
    public async Task Removing_every_work_right_removes_gate_3()
    {
        Account(10034, 10034, "joel", "SAGE:BASE1:READ");
        Assert.Equal(new[] { 1 }, (await GatesOf("joel", 10034)).TokenGates);
    }

    [Fact]
    public async Task Account_whose_role_does_not_exist_has_no_gate()
    {
        Account(10099, 999, "orphan");
        var (token, response, login) = await GatesOf("orphan", 10099);
        Assert.Empty(token);
        Assert.Empty(response);
        Assert.Empty(login.AuthorizedDatabases);
    }

    [Fact]
    public async Task A_rights_change_is_visible_at_the_next_login()
    {
        Account(14, 1, "admin");
        Account(10034, 10034, "joel");
        Assert.Equal(new[] { 1, 3 }, (await GatesOf("joel", 10034)).TokenGates);

        await _auth.SaveUserDetailedPermissionsAsync(14, 10034, new[] { "SAGE:BASE1:READ", "PROC:SHIFTS", "PARAMS:USERS" });
        Assert.Equal(new[] { 1, 2, 3 }, (await GatesOf("joel", 10034)).TokenGates);
    }

    // S2 : PUT privileges/{role} ne doit pas contredire les portes déduites des droits détaillés d'un rôle.
    [Fact]
    public async Task SetPrivileges_is_refused_for_a_role_with_detailed_rights_and_still_works_without()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SetPrivilegesAsync(10034, new List<decimal> { 6 }));
        Assert.Contains("droits détaillés", ex.Message);
        Assert.Equal(new[] { 1m, 3m }, _uow.GetRepo<T_DROIT_ROLE>().Items.Where(d => d.IDROLE == 10034).Select(d => d.IDDROIT).OrderBy(x => x));

        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 50, NOMROLE = "Sans lignes" });
        await _auth.SetPrivilegesAsync(50, new List<decimal> { 6 });
        Assert.Single(_uow.GetRepo<T_DROIT_ROLE>().Items.Where(d => d.IDROLE == 50));
    }

    private const string PermissionCatalogMarker = "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__";

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
