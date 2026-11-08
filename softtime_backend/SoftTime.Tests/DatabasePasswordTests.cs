using System.Text.Json;

using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Serialization;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

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
    private readonly AuthServices _auth;

    public DatabasePasswordTests()
    {
        _auth = new AuthServices(_uow, ServiceMocks.TokenService().Object, _mapper, ServiceMocks.EmailService().Object, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "a" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 5, Login = "g" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "BASE1", Serveur = "srv", TLogin = "sa", TMdp = Stored, TypeAuth = true });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 8, NomBd = "PTG", Serveur = "srv", TLogin = "sa", TMdp = Stored, TypeAuth = true });
    }

    private CatalogServices Catalog(decimal userId, decimal roleId, params int[] rights)
        => new(_uow, _mapper, null!, null!, _discovery, new DatabaseConfigurationAuthorizer(new TestCurrentUser(userId, roleId, rights), _auth), SoftTime.Tests.Http.TestRights.Service(_uow));

    private CatalogServices Admin() => Catalog(1, 1);

    [Fact]
    public void Mapped_dto_has_no_password_and_serializes_without_it()
    {
        // Arrange
        var sage = _mapper.Map<SageDbDto>(_uow.Get<BddSage>().Items[0]);

        // Act
        var pointeuse = _mapper.Map<PointeuseDbDto>(_uow.Get<BddPointeuse>().Items[0]);

        // Assert
        sage.PasswordDefined.Should().BeTrue();
        pointeuse.PasswordDefined.Should().BeTrue();
        sage.Password.Should().BeNull();
        pointeuse.Password.Should().BeNull();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SoftTimeJsonOptions.Configure(options);
        var json = JsonSerializer.Serialize(new object[] { sage, pointeuse }, options);
        json.Should().NotContain(Stored);
        json.Should().NotContain("\"password\"");
        json.Should().Contain("\"passwordDefined\":true");
    }

    [Fact]
    public void PasswordDefined_is_false_when_no_password_is_stored()
    {
        // Act
        var dto = _mapper.Map<SageDbDto>(new BddSage { Id = 1, NomBd = "X", TMdp = "" });

        // Assert
        dto.PasswordDefined.Should().BeFalse();
    }

    [Fact]
    public void Password_sent_by_the_client_still_deserializes_as_input()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dto = JsonSerializer.Deserialize<SageDbDto>("""{"id":0,"serveur":"s","login":"l","password":"FAKE-INPUT-PWD","nomBd":"B"}""", options)!;
        dto.Password.Should().Be("FAKE-INPUT-PWD");
    }

    [Fact]
    public async Task Save_without_password_keeps_the_stored_one()
    {
        // Act
        var result = await Admin().Sage.SaveSageAsync(new SageDbDto(7, "srv", "sa", null, true, "BASE1"));

        // Assert
        _uow.Get<BddSage>().Items[0].TMdp.Should().Be(Stored);
        result.PasswordDefined.Should().BeTrue();
        result.Password.Should().BeNull();
        await Admin().Pointeuse.SavePointeuseAsync(new PointeuseDbDto(8, "srv", "sa", "", true, "PTG", null, false));
        _uow.Get<BddPointeuse>().Items[0].TMdp.Should().Be(Stored);
    }

    [Fact]
    public async Task Save_with_password_replaces_the_stored_one()
    {
        // Act
        var result = await Admin().Sage.SaveSageAsync(new SageDbDto(7, "srv", "sa", Typed, true, "BASE1"));

        // Assert
        _uow.Get<BddSage>().Items[0].TMdp.Should().Be(Typed);
        result.Password.Should().BeNull();
        await Admin().Pointeuse.SavePointeuseAsync(new PointeuseDbDto(8, "srv", "sa", Typed, true, "PTG", null, false));
        _uow.Get<BddPointeuse>().Items[0].TMdp.Should().Be(Typed);
    }

    [Fact]
    public async Task Create_stores_the_password_and_does_not_return_it()
    {
        // Act
        var result = await Admin().Sage.SaveSageAsync(new SageDbDto(0, "srv2", "sa", Typed, true, "NOUVELLE"));

        // Assert
        _uow.Get<BddSage>().Items.Single(s => s.NomBd == "NOUVELLE").TMdp.Should().Be(Typed);
        result.PasswordDefined.Should().BeTrue();
        result.Password.Should().BeNull();
    }

    [Fact]
    public async Task Connection_test_of_an_existing_base_uses_the_stored_password()
    {
        // Act
        await Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7));

        // Assert
        _discovery.LastPassword.Should().Be(Stored);
        await Admin().Pointeuse.TestPointeuseConnectionAsync(new TestPointeuseConnectionDto("srv", true, "sa", "", "PTG", "STANDARD", null, 8));
        _discovery.LastPassword.Should().Be(Stored);
    }

    [Fact]
    public async Task Connection_test_prefers_a_typed_password()
    {
        // Act
        await Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", Typed, "BASE1", "STANDARD", null, 7));

        // Assert
        _discovery.LastPassword.Should().Be(Typed);
    }

    [Fact]
    public async Task Connection_test_of_a_new_base_without_password_sends_none()
    {
        // Act
        await Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, null));

        // Assert
        _discovery.LastPassword.Should().BeNull();
    }

    [Fact]
    public async Task Stored_password_is_never_sent_to_another_server_or_login()
    {
        // Act & Assert
        await FluentActions.Awaiting(() =>
            Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("autre-serveur", true, "sa", null, "BASE1", "STANDARD", null, 7))).Should().ThrowExactlyAsync<DomainException>();
        await FluentActions.Awaiting(() =>
            Admin().Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "autre-login", null, "BASE1", "STANDARD", null, 7))).Should().ThrowExactlyAsync<DomainException>();
        _discovery.LastPassword.Should().BeNull();
    }

    [Fact]
    public async Task Discovery_of_an_existing_base_uses_the_stored_password()
    {
        // Act
        await Admin().Discovery.DiscoverTablesAsync(new DiscoverTablesDto("srv", "BASE1", true, "sa", null, 7, "SAGE"));

        // Assert
        _discovery.LastPassword.Should().Be(Stored);
        await Admin().Discovery.DiscoverColumnsAsync(new DiscoverColumnsDto("srv", "PTG", "T", true, "sa", "", 8, "POINTEUSE"));
        _discovery.LastPassword.Should().Be(Stored);
        await Admin().Discovery.DiscoverDatabasesAsync(new DiscoverDatabasesDto("srv", true, "sa", null, 7, "SAGE"));
        _discovery.LastPassword.Should().Be(Stored);
    }

    [Fact]
    public async Task Stored_password_requires_the_update_permission_on_that_base()
    {
        // Arrange
        // Compte avec la porte Bases mais seulement la lecture : l'usage du mot de passe stocké est refusé.
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = "SAGE:BASE1:READ", IsGranted = true });
        var svc = Catalog(2, 5, Rights.Databases);

        // Act
        Func<Task> act = () =>
            svc.Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7));

        // Assert
        await act.Should().ThrowExactlyAsync<ForbiddenException>();
        _discovery.LastPassword.Should().BeNull();
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = "SAGE:BASE1:UPDATE", IsGranted = true });
        await svc.Sage.TestSageConnectionAsync(new TestSageConnectionDto("srv", true, "sa", null, "BASE1", "STANDARD", null, 7));
        _discovery.LastPassword.Should().Be(Stored);
    }

    [Fact]
    public async Task Login_and_me_responses_carry_no_password()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        var hasher = new PasswordHasher<Responsable>();
        var user = new Responsable { IdResponsable = 9, IdRole = 1, Login = "logintest" };
        user.Password = hasher.HashPassword(user, "FAKE-LOGIN-PWD");
        _uow.Get<Responsable>().Items.Add(user);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var login = await _auth.LoginAsync(new LoginRequest("logintest", "FAKE-LOGIN-PWD"));

        // Act
        var me = await _auth.GetMeAsync(9);

        // Assert
        foreach (var response in new[] { login, me })
        {
            response.AuthorizedDatabases.Should().NotBeEmpty();
            var json = JsonSerializer.Serialize(response, options);
            json.Should().NotContain(Stored);
            response.AuthorizedDatabases.Should().AllSatisfy(d => { d.Password.Should().BeNull(); d.PasswordDefined.Should().BeTrue(); });
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

}
