using System.Net;
using System.Text;

namespace SoftTime.Tests.Http;

/// <summary>Lot D2 : clôture de paie par HTTP. Aucune route de déblocage (405), porte Bases exigée, refus 409 explicite.</summary>
public class PayrollClosuresHttpTests : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/payroll-closures";
    private readonly HttpClient _http;

    public PayrollClosuresHttpTests(ApiFactory factory) => _http = factory.CreateClient();

    [Theory]
    [InlineData("PUT", Url)]
    [InlineData("PATCH", Url)]
    [InlineData("DELETE", Url)]
    [InlineData("PUT", Url + "/periods")]
    [InlineData("DELETE", Url + "/periods")]
    [InlineData("POST", Url + "/state")]
    [InlineData("DELETE", Url + "/state")]
    public async Task UnlockOrModification_Return405(string methode, string url)
    {
        var (code, _) = await Envoyer("admin", new HttpMethod(methode), url);

        code.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("DELETE", Url + "/1")]
    [InlineData("POST", Url + "/1/deblocage")]
    [InlineData("POST", Url + "/1/annuler")]
    [InlineData("POST", Url + "/unlock")]
    public async Task UnlockRoutes_DoNotExist(string methode, string url)
    {
        var (code, _) = await Envoyer("admin", new HttpMethod(methode), url);

        code.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Close_WithoutAnySend_Returns409WithTheExplicitMessage()
    {
        var (code, texte) = await Envoyer("admin", HttpMethod.Post, Url, """{"from":"2025-01-06","to":"2025-01-12"}""");

        code.Should().Be(HttpStatusCode.Conflict);
        texte.Should().Contain("Aucun envoi en paie n'existe pour cette période exacte");
    }

    [Fact]
    public async Task Close_WithoutTheDatabasesGate_Returns403()
    {
        var (code, _) = await Envoyer("nogate", HttpMethod.Post, Url, """{"from":"2025-01-06","to":"2025-01-12"}""");

        code.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task State_IsReadableByAWorkingUser()
    {
        // Utilisateur de traitement de la base (porte Traitements, lecture de la base), sans droit de clôture.
        var (code, texte) = await Envoyer("ptenone", HttpMethod.Get, $"{Url}/state?from=2025-01-06&to=2025-01-12");

        code.Should().Be(HttpStatusCode.OK);
        texte.Should().Contain("\"cloturee\":false");
    }

    private async Task<(HttpStatusCode Code, string Texte)> Envoyer(string profil, HttpMethod methode, string url, string? corps = null)
    {
        var req = new HttpRequestMessage(methode, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiFactory.TokenFor(profil)}");
        req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
        if (corps is not null)
            req.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }
}
