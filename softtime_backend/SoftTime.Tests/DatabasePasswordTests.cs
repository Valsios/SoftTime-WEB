using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Serialization;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Models;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>H4 : le mot de passe SQL des bases n'est jamais renvoyé, conservé s'il n'est pas modifié, et le test de connexion peut utiliser le mot de passe stocké.</summary>
public class DatabasePasswordTests
{
    // Valeurs factices évidentes : aucun mot de passe réel.
    private const string Stored = "FAKE-STORED-PWD";
    private const string Typed = "FAKE-TYPED-PWD";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
    private readonly RecordingDiscovery _discovery = new();
    private readonly AuthService _auth;

    public DatabasePasswordTests()
    {
        _auth = new AuthService(_uow, new FakeTokens(), _mapper, new FakeEmail(), new SoftTime.Tests.Http.AdminCurrentUser(), new PasswordHasher<T_RESPONSABLE>());
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 5, NOMROLE = "Gestionnaire" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "a" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 5, LOGIN = "g" });
        _uow.GetRepo<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 7, NOM_BD = "BASE1", SERVEUR = "srv", TLOGIN = "sa", TMDP = Stored, TYPE_AUTH = true });
        _uow.GetRepo<T_BDD_POINTEUSE>().Items.Add(new T_BDD_POINTEUSE { ID = 8, NOM_BD = "PTG", SERVEUR = "srv", TLOGIN = "sa", TMDP = Stored, TYPE_AUTH = true });
    }

    private CatalogService Catalog(decimal userId, decimal roleId, params int[] rights)
        => new(_uow, _mapper, null!, null!, _discovery, new DatabaseConfigurationAuthorizer(new FakeUser(userId, roleId, rights), _auth));

    private CatalogService Admin() => Catalog(1, 1);

    [Fact]
    public void Mapped_dto_has_no_password_and_serializes_without_it()
    {
        var sage = _mapper.Map<SageDbDto>(_uow.GetRepo<T_BDD_SAGE>().Items[0]);
        var pointeuse = _mapper.Map<PointeuseDbDto>(_uow.GetRepo<T_BDD_POINTEUSE>().Items[0]);
        Assert.True(sage.PasswordDefined);
        Assert.True(pointeuse.PasswordDefined);
        Assert.Null(sage.Password);
        Assert.Null(pointeuse.Password);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SoftTimeJsonOptions.Configure(options);
        var json = JsonSerializer.Serialize(new object[] { sage, pointeuse }, options);
        Assert.DoesNotContain(Stored, json);
        Assert.DoesNotContain("\"password\"", json);
        Assert.Contains("\"passwordDefined\":true", json);
    }

    [Fact]
    public void PasswordDefined_is_false_when_no_password_is_stored()
    {
        var dto = _mapper.Map<SageDbDto>(new T_BDD_SAGE { ID = 1, NOM_BD = "X", TMDP = "" });
        Assert.False(dto.PasswordDefined);
    }

    [Fact]
    public void Password_sent_by_the_client_still_deserializes_as_input()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dto = JsonSerializer.Deserialize<SageDbDto>("""{"id":0,"serveur":"s","login":"l","password":"FAKE-INPUT-PWD","nomBd":"B"}""", options)!;
        Assert.Equal("FAKE-INPUT-PWD", dto.Password);
    }

    [Fact]
    public async Task Save_without_password_keeps_the_stored_one()
    {
        var result = await Admin().SaveSageAsync(new SageDbDto(7, "srv", "sa", null, true, "BASE1"));
        Assert.Equal(Stored, _uow.GetRepo<T_BDD_SAGE>().Items[0].TMDP);
        Assert.True(result.PasswordDefined);
        Assert.Null(result.Password);

        await Admin().SavePointeuseAsync(new PointeuseDbDto(8, "srv", "sa", "", true, "PTG", null, false));
        Assert.Equal(Stored, _uow.GetRepo<T_BDD_POINTEUSE>().Items[0].TMDP);
    }

    [Fact]
    public async Task Save_with_password_replaces_the_stored_one()
    {
        var result = await Admin().SaveSageAsync(new SageDbDto(7, "srv", "sa", Typed, true, "BASE1"));
        Assert.Equal(Typed, _uow.GetRepo<T_BDD_SAGE>().Items[0].TMDP);
        Assert.Null(result.Password);

        await Admin().SavePointeuseAsync(new PointeuseDbDto(8, "srv", "sa", Typed, true, "PTG", null, false));
        Assert.Equal(Typed, _uow.GetRepo<T_BDD_POINTEUSE>().Items[0].TMDP);
    }

    [Fact]
    public async Task Create_stores_the_password_and_does_not_return_it()
    {
        var result = await Admin().SaveSageAsync(new SageDbDto(0, "srv2", "sa", Typed, true, "NOUVELLE"));
        Assert.Equal(Typed, _uow.GetRepo<T_BDD_SAGE>().Items.Single(s => s.NOM_BD == "NOUVELLE").TMDP);
        Assert.True(result.PasswordDefined);
        Assert.Null(result.Password);
    }

    [Fact]
    public async Task Connection_test_of_an_existing_base_uses_the_stored_password()
    {
        await Admin().TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7));
        Assert.Equal(Stored, _discovery.LastPassword);

        await Admin().TestPointeuseConnectionAsync(new TestPointeuseConnectionDto("srv", true, "sa", "", "PTG", "STANDARD", null, 8));
        Assert.Equal(Stored, _discovery.LastPassword);
    }

    [Fact]
    public async Task Connection_test_prefers_a_typed_password()
    {
        await Admin().TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", Typed, "BASE1", "STANDARD", null, 7));
        Assert.Equal(Typed, _discovery.LastPassword);
    }

    [Fact]
    public async Task Connection_test_of_a_new_base_without_password_sends_none()
    {
        await Admin().TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, null));
        Assert.Null(_discovery.LastPassword);
    }

    [Fact]
    public async Task Stored_password_is_never_sent_to_another_server_or_login()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Admin().TestSageConnectionAsync(new TestSageConnectionDto("autre-serveur", true, "sa", null, "BASE1", "STANDARD", null, 7)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Admin().TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "autre-login", null, "BASE1", "STANDARD", null, 7)));
        Assert.Null(_discovery.LastPassword);
    }

    [Fact]
    public async Task Discovery_of_an_existing_base_uses_the_stored_password()
    {
        await Admin().DiscoverTablesAsync(new DiscoverTablesDto("srv", "BASE1", true, "sa", null, 7, "SAGE"));
        Assert.Equal(Stored, _discovery.LastPassword);
        await Admin().DiscoverColumnsAsync(new DiscoverColumnsDto("srv", "PTG", "T", true, "sa", "", 8, "POINTEUSE"));
        Assert.Equal(Stored, _discovery.LastPassword);
        await Admin().DiscoverDatabasesAsync(new DiscoverDatabasesDto("srv", true, "sa", null, 7, "SAGE"));
        Assert.Equal(Stored, _discovery.LastPassword);
    }

    [Fact]
    public async Task Stored_password_requires_the_update_permission_on_that_base()
    {
        // Compte avec la porte Bases mais seulement la lecture : l'usage du mot de passe stocké est refusé.
        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 2, PERMISSION_CODE = "SAGE:BASE1:READ", IS_GRANTED = true });
        var svc = Catalog(2, 5, Rights.Databases);
        await Assert.ThrowsAsync<ForbiddenOperationException>(() =>
            svc.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7)));
        Assert.Null(_discovery.LastPassword);

        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 2, PERMISSION_CODE = "SAGE:BASE1:UPDATE", IS_GRANTED = true });
        await svc.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7));
        Assert.Equal(Stored, _discovery.LastPassword);
    }

    [Fact]
    public async Task Login_and_me_responses_carry_no_password()
    {
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        var hasher = new PasswordHasher<T_RESPONSABLE>();
        var user = new T_RESPONSABLE { IDRESPONSABLE = 9, IDROLE = 1, LOGIN = "logintest" };
        user.PASSWORD = hasher.HashPassword(user, "FAKE-LOGIN-PWD");
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var login = await _auth.LoginAsync(new LoginRequest("logintest", "FAKE-LOGIN-PWD"));
        var me = await _auth.GetMeAsync(9);
        foreach (var response in new[] { login, me })
        {
            Assert.NotEmpty(response.AuthorizedDatabases);
            var json = JsonSerializer.Serialize(response, options);
            Assert.DoesNotContain(Stored, json);
            Assert.All(response.AuthorizedDatabases, d => { Assert.Null(d.Password); Assert.True(d.PasswordDefined); });
        }
    }

    private sealed class RecordingDiscovery : IExternalDiscoveryService
    {
        public string? LastPassword { get; private set; }
        private IReadOnlyList<string> Record(string? password) { LastPassword = password; return new[] { "T_SAL", "T_HST_AFFECTATION", "T_DEPARTEMENT", "T_GHRCAL_SOCIETE", "T_CST", "USERINFO", "CHECKINOUT" }; }
        public Task<IReadOnlyList<string>> ListServersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListDatabasesAsync(string serveur, bool sqlAuth, string? login, string? password, CancellationToken ct = default) => Task.FromResult(Record(password));
        public Task<IReadOnlyList<string>> ListTablesAsync(string serveur, string baseDb, bool sqlAuth, string? login, string? password, CancellationToken ct = default) => Task.FromResult(Record(password));
        public Task<IReadOnlyList<string>> ListColumnsAsync(string serveur, string baseDb, string table, bool sqlAuth, string? login, string? password, CancellationToken ct = default) => Task.FromResult(Record(password));
        public Task<IReadOnlyList<string>> ListDistinctValuesAsync(string serveur, string baseDb, string table, string column, bool sqlAuth, string? login, string? password, CancellationToken ct = default) => Task.FromResult(Record(password));
    }

    private sealed class FakeUser : ICurrentUser
    {
        public FakeUser(decimal userId, decimal roleId, int[] rights) { UserId = userId; RoleId = roleId; Rights = rights; }
        public decimal? UserId { get; }
        public string? Login => $"u{UserId}";
        public decimal? RoleId { get; }
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights { get; }
        public bool HasRight(int droit) => RoleId == 1 || Rights.Contains(droit);
    }

    private sealed class FakeTokens : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights) => "FAKE_TOKEN";
    }

    private sealed class FakeEmail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
