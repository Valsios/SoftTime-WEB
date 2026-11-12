using System.Net;

using Microsoft.Extensions.DependencyInjection;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Écritures de l'écran Privilèges, de bout en bout (jeton, filtres, contrôleur, service réel, magasin en mémoire).
/// </summary>
public class PrivilegesWriteHttpTests : IClassFixture<ApiFactory>
{
    private const decimal EmployeeRoleId = 90;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public PrivilegesWriteHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task SaveRolePermissions_EmployeeRoleWithSelfRead_IsSaved()
    {
        // Arrange
        var body = $$"""{"grantedPermissions":["{{PermissionCatalog.SelfRead}}"]}""";

        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Put, $"/api/v1/privileges/role/{EmployeeRoleId}", body));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, await resp.Content.ReadAsStringAsync());
        _f.Store.Get<RolePermission>().Items
            .Where(p => p.IdRole == EmployeeRoleId)
            .Select(p => p.PermissionCode)
            .Should().Equal(PermissionCatalog.SelfRead);
    }

    /// <summary>Toutes les écritures de l'écran Privilèges, avec un corps valide.</summary>
    public static TheoryData<string, string, string?> Writes() => new()
    {
        { "PUT", "/api/v1/privileges/role/91", """{"grantedPermissions":["PROC:SHIFTS"]}""" },
        { "PUT", "/api/v1/privileges/91", "[3]" },
        { "PUT", "/api/v1/privileges/user/10050", """{"grantedPermissions":["PROC:SHIFTS"]}""" },
        { "POST", "/api/v1/privileges/user/10050/reset", null },
    };

    [Fact]
    public async Task GetUserPermissions_Administrator_ReturnsEachRightWithItsSource()
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Get, "/api/v1/privileges/user/10034"));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var rights = doc.RootElement.GetProperty("data").GetProperty("rights").EnumerateArray()
            .ToDictionary(r => r.GetProperty("code").GetString()!, r => r.GetProperty("source").GetString());
        rights["PROC:SHIFTS"].Should().Be("ROLE");
        rights["PROC:PUNCHES"].Should().Be("REMOVED");
    }

    [Theory]
    [InlineData("anonyme", HttpStatusCode.Unauthorized)]
    [InlineData("privreader", HttpStatusCode.Forbidden)]
    public async Task RemoveLegacyPermissions_NonAdministrator_IsRefused(string profile, HttpStatusCode expected)
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Post, "/api/v1/privileges/user/10050/legacy-permissions/remove",
            """{"codes":["SAGE:ANCIENNE"]}"""));

        // Assert
        resp.StatusCode.Should().Be(expected);
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task RemoveLegacyPermissions_Administrator_RemovesTheConfirmedCode()
    {
        // Arrange
        _f.Store.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 10051, PermissionCode = "SAGE:ANCIENNE", IsGranted = true });

        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Post, "/api/v1/privileges/user/10051/legacy-permissions/remove",
            """{"codes":["SAGE:ANCIENNE"]}"""));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, await resp.Content.ReadAsStringAsync());
        _f.Store.Get<UserPermission>().Items.Should().NotContain(p => p.IdResponsable == 10051 && p.PermissionCode == "SAGE:ANCIENNE");
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task PrivilegesWrite_Anonymous_Returns401(string method, string url, string? body)
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("anonyme", new HttpMethod(method), url, body));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task PrivilegesWrite_NonAdministratorWithPrivilegesScreen_Returns403AndChangesNothing(string method, string url, string? body)
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var resp = await _http.SendAsync(_f.Request("privreader", new HttpMethod(method), url, body));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Cette opération est réservée aux administrateurs.");
        _f.Snapshot().Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task PrivilegesWrite_Administrator_Succeeds(string method, string url, string? body)
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("admin", new HttpMethod(method), url, body));

        // Assert
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaveUserPermissions_UnknownCode_Returns400AndChangesNothing()
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Put, "/api/v1/privileges/user/10052",
            """{"grantedPermissions":["PROC:SHIFTS","INVALID:HACK:CODE"]}"""));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Codes de permission inconnus : INVALID:HACK:CODE.");
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task SaveUserPermissions_ValidCodes_AreNormalizedAndSaved()
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Put, "/api/v1/privileges/user/10052",
            """{"grantedPermissions":["proc:shifts","sage:SFOI_PAIE2025:read"]}"""));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, await resp.Content.ReadAsStringAsync());
        // Migration 006 : les lignes sont des écarts par rapport au rôle ; on vérifie les droits effectifs obtenus.
        using var scope = _f.Services.CreateScope();
        var rights = await scope.ServiceProvider.GetRequiredService<IEffectiveRightsService>().GetAsync(10052, fresh: true);
        rights.Permissions.Should().Contain(new[] { "PROC:SHIFTS", "SAGE:SFOI_PAIE2025:READ" });
        // Code normalisé à l'enregistrement (action en majuscules, nom de base conservé).
        _f.Store.Get<UserPermission>().Items.Where(p => p.IdResponsable == 10052)
            .Select(p => p.PermissionCode).Should().Contain("SAGE:SFOI_PAIE2025:READ");
    }
}
