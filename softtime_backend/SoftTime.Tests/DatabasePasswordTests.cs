using System.Text.Json;

using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Serialization;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>H4 : le mot de passe SQL des bases n'est jamais renvoyé, conservé s'il n'est pas modifié, et le test de connexion peut utiliser le mot de passe stocké.</summary>
public class DatabasePasswordTests
{
    // Valeurs factices évidentes : aucun mot de passe réel.
    private const string Stored = "FAKE-STORED-PWD";
    private const string Typed = "FAKE-TYPED-PWD";

    private readonly SnapshotInMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
    private readonly RecordingDiscovery _discovery = new();
    private readonly AuthServices _auth;

    public DatabasePasswordTests()
    {
        _auth = new AuthServices(_uow, new FakeTokens(), _mapper, new FakeEmail(), new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "a" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 5, Login = "g" });
        _uow.GetRepo<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "BASE1", Serveur = "srv", TLogin = "sa", TMdp = Stored, TypeAuth = true });
        _uow.GetRepo<BddPointeuse>().Items.Add(new BddPointeuse { Id = 8, NomBd = "PTG", Serveur = "srv", TLogin = "sa", TMdp = Stored, TypeAuth = true });
    }

    private CatalogServices Catalog(decimal userId, decimal roleId, params int[] rights)
        => new(_uow, _mapper, null!, null!, _discovery, new DatabaseConfigurationAuthorizer(new FakeUser(userId, roleId, rights), _auth), SoftTime.Tests.Http.TestRights.Service(_uow));

    private CatalogServices Admin() => Catalog(1, 1);

    [Fact]
    public void Mapped_dto_has_no_password_and_serializes_without_it()
    {
        var sage = _mapper.Map<SageDbDto>(_uow.GetRepo<BddSage>().Items[0]);
        var pointeuse = _mapper.Map<PointeuseDbDto>(_uow.GetRepo<BddPointeuse>().Items[0]);
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
        var dto = _mapper.Map<SageDbDto>(new BddSage { Id = 1, NomBd = "X", TMdp = "" });
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
        var result = await Admin().Sage.SaveSageAsync(new SageDbDto(7, "srv", "sa", null, true, "BASE1"));
        Assert.Equal(Stored, _uow.GetRepo<BddSage>().Items[0].TMdp);
        Assert.True(result.PasswordDefined);
        Assert.Null(result.Password);

        await Admin().Pointeuse.SavePointeuseAsync(new PointeuseDbDto(8, "srv", "sa", "", true, "PTG", null, false));
        Assert.Equal(Stored, _uow.GetRepo<BddPointeuse>().Items[0].TMdp);
    }

    [Fact]
    public async Task Save_with_password_replaces_the_stored_one()
    {
        var result = await Admin().Sage.SaveSageAsync(new SageDbDto(7, "srv", "sa", Typed, true, "BASE1"));
        Assert.Equal(Typed, _uow.GetRepo<BddSage>().Items[0].TMdp);
        Assert.Null(result.Password);

        await Admin().Pointeuse.SavePointeuseAsync(new PointeuseDbDto(8, "srv", "sa", Typed, true, "PTG", null, false));
        Assert.Equal(Typed, _uow.GetRepo<BddPointeuse>().Items[0].TMdp);
    }

    [Fact]
    public async Task Create_stores_the_password_and_does_not_return_it()
    {
        var result = await Admin().Sage.SaveSageAsync(new SageDbDto(0, "srv2", "sa", Typed, true, "NOUVELLE"));
        Assert.Equal(Typed, _uow.GetRepo<BddSage>().Items.Single(s => s.NomBd == "NOUVELLE").TMdp);
        Assert.True(result.PasswordDefined);
        Assert.Null(result.Password);
    }

    [Fact]
    public async Task Connection_test_of_an_existing_base_uses_the_stored_password()
    {
        await Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7));
        Assert.Equal(Stored, _discovery.LastPassword);

        await Admin().Pointeuse.TestPointeuseConnectionAsync(new TestPointeuseConnectionDto("srv", true, "sa", "", "PTG", "STANDARD", null, 8));
        Assert.Equal(Stored, _discovery.LastPassword);
    }

    [Fact]
    public async Task Connection_test_prefers_a_typed_password()
    {
        await Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", Typed, "BASE1", "STANDARD", null, 7));
        Assert.Equal(Typed, _discovery.LastPassword);
    }

    [Fact]
    public async Task Connection_test_of_a_new_base_without_password_sends_none()
    {
        await Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, null));
        Assert.Null(_discovery.LastPassword);
    }

    [Fact]
    public async Task Stored_password_is_never_sent_to_another_server_or_login()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("autre-serveur", true, "sa", null, "BASE1", "STANDARD", null, 7)));
        await Assert.ThrowsAsync<DomainException>(() =>
            Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "autre-login", null, "BASE1", "STANDARD", null, 7)));
        Assert.Null(_discovery.LastPassword);
    }

    [Fact]
    public async Task Discovery_of_an_existing_base_uses_the_stored_password()
    {
        await Admin().Discovery.DiscoverTablesAsync(new DiscoverTablesDto("srv", "BASE1", true, "sa", null, 7, "SAGE"));
        Assert.Equal(Stored, _discovery.LastPassword);
        await Admin().Discovery.DiscoverColumnsAsync(new DiscoverColumnsDto("srv", "PTG", "T", true, "sa", "", 8, "POINTEUSE"));
        Assert.Equal(Stored, _discovery.LastPassword);
        await Admin().Discovery.DiscoverDatabasesAsync(new DiscoverDatabasesDto("srv", true, "sa", null, 7, "SAGE"));
        Assert.Equal(Stored, _discovery.LastPassword);
    }

    [Fact]
    public async Task Stored_password_requires_the_update_permission_on_that_base()
    {
        // Compte avec la porte Bases mais seulement la lecture : l'usage du mot de passe stocké est refusé.
        _uow.GetRepo<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = "SAGE:BASE1:READ", IsGranted = true });
        var svc = Catalog(2, 5, Rights.Databases);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7)));
        Assert.Null(_discovery.LastPassword);

        _uow.GetRepo<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = "SAGE:BASE1:UPDATE", IsGranted = true });
        await svc.Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7));
        Assert.Equal(Stored, _discovery.LastPassword);
    }

    [Fact]
    public async Task Login_and_me_responses_carry_no_password()
    {
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        var hasher = new PasswordHasher<Responsable>();
        var user = new Responsable { IdResponsable = 9, IdRole = 1, Login = "logintest" };
        user.Password = hasher.HashPassword(user, "FAKE-LOGIN-PWD");
        _uow.GetRepo<Responsable>().Items.Add(user);

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
        public Task<IReadOnlyList<string>> ListDatabasesAsync(SqlServerCredentials credentials, CancellationToken ct = default) => Task.FromResult(Record(credentials.Password));
        public Task<IReadOnlyList<string>> ListTablesAsync(SqlServerCredentials credentials, string baseDb, CancellationToken ct = default) => Task.FromResult(Record(credentials.Password));
        public Task<IReadOnlyList<string>> ListColumnsAsync(SqlServerCredentials credentials, string baseDb, string table, CancellationToken ct = default) => Task.FromResult(Record(credentials.Password));
        public Task<IReadOnlyList<string>> ListDistinctValuesAsync(SqlServerCredentials credentials, string baseDb, string table, string column, CancellationToken ct = default) => Task.FromResult(Record(credentials.Password));
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
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
