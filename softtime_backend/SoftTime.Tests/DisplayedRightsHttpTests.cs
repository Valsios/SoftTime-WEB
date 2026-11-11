using SoftTime.Tests.Http;

namespace SoftTime.Tests;

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
    public async Task GetRolePermissions_GateOnlyRoleAfterStartupMigration_ShowsReadButNoConfiguration()
    {
        // Act
        // Rôle 10034 du jeu de test : portes 1 et 3 sans ligne, converti en droits détaillés par la migration du démarrage.
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Get, "/api/v1/privileges/role/10034"));

        // Assert
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var perms = doc.RootElement.GetProperty("data").GetProperty("grantedPermissions").EnumerateArray().Select(e => e.GetString()!).ToList();
        perms.Should().Contain("sage.all.read");
        perms.Should().NotContain("sage.create");
        perms.Should().NotContain("sage.all.update");
        perms.Should().NotContain("sage.all.delete");
        doc.RootElement.GetProperty("data").GetProperty("isConfigured").GetBoolean().Should().BeTrue();
    }
}
