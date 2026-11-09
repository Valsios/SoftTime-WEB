using System.Net;
using System.Text;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>PUT api/v1/sage-databases/{id} : l'identifiant de l'URL prime sur celui du corps ; un doublon serveur + nom répond 409.</summary>
public class SageDatabaseUpdateHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public SageDatabaseUpdateHttpTests(ApiFactory factory)
    {
        _f = factory;
        _f.ResetStore();
        _http = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> PutAsync(int idUrl, string body)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sage-databases/{idUrl}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiFactory.TokenFor("admin")}");
        return await _http.SendAsync(req);
    }

    private BddSage Row(int id) => _f.Store.Get<BddSage>().Items.Single(s => s.Id == id);

    [Fact]
    public async Task UpdateAsync_BodyIdDiffersFromUrlId_UpdatesTheUrlRowOnly()
    {
        // Arrange
        var avant3035 = Row(3035).NomBd;

        // Act : le corps désigne 3035, l'URL 3036.
        var reponse = await PutAsync(3036, """{"id":3035,"serveur":"invalid.host.test","sqlAuth":true,"nomBd":"MODIF_PAR_URL","typeBase":"STANDARD"}""");

        // Assert
        reponse.StatusCode.Should().Be(HttpStatusCode.OK);
        Row(3036).NomBd.Should().Be("MODIF_PAR_URL");
        Row(3035).NomBd.Should().Be(avant3035);
    }

    [Fact]
    public async Task UpdateAsync_RenameToExistingServerAndName_Returns409()
    {
        // Arrange
        var existante = Row(3035).NomBd;

        // Act
        var reponse = await PutAsync(3036, $$"""{"id":3036,"serveur":"invalid.host.test","sqlAuth":true,"nomBd":"{{existante}}","typeBase":"STANDARD"}""");

        // Assert
        reponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await reponse.Content.ReadAsStringAsync()).Should().Contain("déjà enregistrée");
        Row(3036).NomBd.Should().NotBe(existante);
    }
}
