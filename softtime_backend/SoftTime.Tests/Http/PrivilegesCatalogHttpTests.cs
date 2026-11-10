using System.Net;
using System.Text.Json;

using SoftTime.Application.Security;
using SoftTime.Domain;

namespace SoftTime.Tests.Http;

/// <summary>
/// GET privileges/catalog : codes connus groupés par domaine, source unique (PermissionCatalog). Même protection que les
/// autres lectures de Privilèges : porte Paramètres ET PARAMS:PRIVILEGES ou PARAMS:ROLES (ou administrateur).
/// </summary>
public class PrivilegesCatalogHttpTests : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/privileges/catalog";

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public PrivilegesCatalogHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    [Fact]
    public void Domains_EveryKnownCode_AppearsOnceInItsDomain()
    {
        // Act
        var domains = PermissionCatalog.Domains();

        // Assert
        domains.SelectMany(d => d.Codes).Should().BeEquivalentTo(PermissionCatalog.KnownStatic);
        domains.SelectMany(d => d.Codes).Should().OnlyHaveUniqueItems();
        domains.Should().OnlyContain(d => d.Codes.All(c => c.StartsWith(d.Key + ":", StringComparison.Ordinal)));
        domains.Select(d => (d.Key, d.Gate)).Should().Equal(
            ("PARAMS", Rights.Parameters), ("PROC", Rights.Processing), ("OTHER", Rights.Other),
            ("TRACE", Rights.Traceability), ("REPORTS", Rights.Reports), ("SELF", 0));
    }

    [Fact]
    public async Task GetCatalog_Anonymous_Returns401()
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("anonyme", HttpMethod.Get, Url));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("joel")]          // sans porte Paramètres
    [InlineData("paramsother")]   // porte Paramètres, sans PARAMS:PRIVILEGES ni PARAMS:ROLES
    public async Task GetCatalog_WithoutPrivilegesRead_Returns403(string profile)
    {
        // Act
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, Url));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("privreader")]
    [InlineData("rolesreader")]
    public async Task GetCatalog_AdministratorOrPrivilegesReader_ReturnsTheDomains(string profile)
    {
        // Act
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, Url));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var domains = doc.RootElement.GetProperty("data").EnumerateArray()
            .ToDictionary(d => d.GetProperty("key").GetString()!, d => d.GetProperty("codes").EnumerateArray().Select(c => c.GetString()).ToList());
        domains["SELF"].Should().Equal(PermissionCatalog.SelfRead);
        domains["PROC"].Should().Contain("PROC:SHIFTS:DELETE");
    }
}
