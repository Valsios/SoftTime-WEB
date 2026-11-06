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
