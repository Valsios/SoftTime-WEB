using System.Net;

namespace SoftTime.Tests.Http;

/// <summary>Lot D1 : instantanés des envois en paie, en lecture seule (405 sur toute écriture), porte Bases exigée.</summary>
public class PayrollSnapshotsHttpTests : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/payroll-snapshots";
    private readonly HttpClient _http;

    public PayrollSnapshotsHttpTests(ApiFactory factory) => _http = factory.CreateClient();

    [Fact]
    public async Task List_Admin_Returns200()
    {
        var (code, _) = await Envoyer("admin", HttpMethod.Get, $"{Url}?from=2026-09-14&to=2026-10-11");

        code.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("POST", Url)]
    [InlineData("PUT", Url)]
    [InlineData("PATCH", Url)]
    [InlineData("DELETE", Url)]
    [InlineData("PUT", Url + "/1")]
    [InlineData("DELETE", Url + "/1")]
    [InlineData("POST", Url + "/1/verification")]
    public async Task WriteMethods_Return405(string methode, string url)
    {
        var (code, _) = await Envoyer("admin", new HttpMethod(methode), url);

        code.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task WithoutTheDatabasesGate_Returns403()
    {
        var (code, _) = await Envoyer("nogate", HttpMethod.Get, $"{Url}?from=2026-09-14&to=2026-10-11");

        code.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(HttpStatusCode Code, string Texte)> Envoyer(string profil, HttpMethod methode, string url)
    {
        var req = new HttpRequestMessage(methode, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiFactory.TokenFor(profil)}");
        req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
        var resp = await _http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }
}
