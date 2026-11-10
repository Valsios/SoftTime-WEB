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
    public async Task GetRolePermissions_DatabaseGateWithoutRows_ShowsNoConfiguration()
    {
        // Act
        // Rôle 10034 du jeu de test : portes 1 et 3, aucune ligne détaillée.
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Get, "/api/v1/privileges/role/10034"));

        // Assert
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var perms = doc.RootElement.GetProperty("data").GetProperty("grantedPermissions").EnumerateArray().Select(e => e.GetString()!).ToList();
        perms.Should().Contain("SAGE:SFOI_PAIE2025:READ");
        perms.Should().NotContain("SAGE:CREATE");
        perms.Should().NotContain("SAGE:SFOI_PAIE2025:UPDATE");
        perms.Should().NotContain("SAGE:SFOI_PAIE2025:DELETE");
        doc.RootElement.GetProperty("data").GetProperty("isConfigured").GetBoolean().Should().BeFalse();
    }
}
