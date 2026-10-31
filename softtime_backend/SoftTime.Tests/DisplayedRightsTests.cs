using AutoMapper;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
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
    private readonly AuthService _auth;

    public DisplayedRightsTests()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthService(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), TestRights.Service(_uow), new PasswordHasher<T_RESPONSABLE>());

        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 5, NOMROLE = "Sans lignes" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 6, NOMROLE = "Avec lignes" });
        _uow.GetRepo<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "BASE1" });
        _uow.GetRepo<T_BDD_POINTEUSE>().Items.Add(new T_BDD_POINTEUSE { ID = 1, NOM_BD = "PTG" });
        // Rôle 5 : portes Bases + Traitements, aucune ligne. Rôle 6 : mêmes portes, lignes détaillées.
        foreach (var role in new decimal[] { 5, 6 })
        {
            _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = role, IDDROIT = 1 });
            _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = role, IDDROIT = 3 });
        }
        _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 6, PERMISSION_CODE = "SAGE:CREATE", IS_GRANTED = true });
        _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 6, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 15, IDROLE = 5, LOGIN = "sanslignes" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 16, IDROLE = 6, LOGIN = "aveclignes" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 17, IDROLE = 5, LOGIN = "ajustements" });
        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 17, PERMISSION_CODE = "SAGE:CREATE", IS_GRANTED = true });
        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 17, PERMISSION_CODE = "SAGE:BASE1:READ", IS_GRANTED = true });
    }

    [Fact]
    public async Task Role_without_rows_shows_database_read_but_no_configuration()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);
        Assert.False(role.IsConfigured);
        Assert.Contains("SAGE:BASE1:READ", role.GrantedPermissions);
        Assert.Contains("POINTEUSE:PTG:READ", role.GrantedPermissions);
        Assert.Contains("PROC:SHIFTS", role.GrantedPermissions);
        Assert.All(Configuration, c => Assert.DoesNotContain(c, role.GrantedPermissions));
    }

    [Fact]
    public async Task Account_of_that_role_without_adjustment_shows_the_same_rights()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);
        var user = await _auth.GetUserDetailedPermissionsAsync(15);
        Assert.False(user.IsCustomized);
        Assert.True(role.GrantedPermissions.ToHashSet().SetEquals(user.GrantedPermissions));
        Assert.All(Configuration, c => Assert.DoesNotContain(c, user.GrantedPermissions));
    }

    [Fact]
    public async Task Role_with_rows_is_unchanged()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(6);
        Assert.True(role.IsConfigured);
        Assert.True(role.GrantedPermissions.ToHashSet().SetEquals(new[] { "SAGE:CREATE", "PROC:SHIFTS" }));
        var user = await _auth.GetUserDetailedPermissionsAsync(16);
        Assert.True(user.GrantedPermissions.ToHashSet().SetEquals(new[] { "SAGE:CREATE", "PROC:SHIFTS" }));
    }

    [Fact]
    public async Task Account_with_personal_rows_keeps_its_list()
    {
        var user = await _auth.GetUserDetailedPermissionsAsync(17);
        Assert.True(user.IsCustomized);
        Assert.True(user.GrantedPermissions.ToHashSet().SetEquals(new[] { "SAGE:CREATE", "SAGE:BASE1:READ" }));
    }

    [Fact]
    public async Task Administrator_keeps_every_right_including_configuration()
    {
        var user = await _auth.GetUserDetailedPermissionsAsync(1);
        var role = await _auth.GetRoleDetailedPermissionsAsync(1);
        Assert.All(Configuration, c => Assert.Contains(c, user.GrantedPermissions));
        Assert.All(Configuration, c => Assert.Contains(c, role.GrantedPermissions));
    }

    [Fact]
    public async Task Unknown_role_shows_no_right()
    {
        var role = await _auth.GetRoleDetailedPermissionsAsync(999);
        Assert.False(role.IsConfigured);
        Assert.Empty(role.GrantedPermissions);
    }

    [Fact]
    public async Task Database_gate_without_any_database_is_not_carried_by_the_displayed_rights()
    {
        // Cas limite documenté : sans base, aucun droit SAGE/POINTEUSE n'est affiché, donc réenregistrer ces droits
        // ne conserve pas la porte Bases (elle ne donnerait de toute façon accès à rien).
        _uow.GetRepo<T_BDD_SAGE>().Items.Clear();
        _uow.GetRepo<T_BDD_POINTEUSE>().Items.Clear();
        var role = await _auth.GetRoleDetailedPermissionsAsync(5);
        await _auth.SaveRoleDetailedPermissionsAsync(1, 5, role.GrantedPermissions);
        var gates = _uow.GetRepo<T_DROIT_ROLE>().Items.Where(d => d.IDROLE == 5).Select(d => (int)d.IDDROIT).OrderBy(x => x);
        Assert.Equal(new[] { 3 }, gates);
    }

    private sealed class Token : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}

/// <summary>Mêmes constats par HTTP : la route GET privileges/role/{id} d'un rôle sans lignes ne montre plus la configuration.</summary>
public class DisplayedRightsHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public DisplayedRightsHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task Role_route_of_a_role_with_the_database_gate_and_no_rows_has_no_configuration()
    {
        // Rôle 10034 du jeu de test : portes 1 et 3, aucune ligne détaillée.
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Get, "/api/privileges/role/10034"));
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var perms = doc.RootElement.GetProperty("grantedPermissions").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains("SAGE:SFOI_PAIE2025:READ", perms);
        Assert.DoesNotContain("SAGE:CREATE", perms);
        Assert.DoesNotContain("SAGE:SFOI_PAIE2025:UPDATE", perms);
        Assert.DoesNotContain("SAGE:SFOI_PAIE2025:DELETE", perms);
        Assert.False(doc.RootElement.GetProperty("isConfigured").GetBoolean());
    }
}
