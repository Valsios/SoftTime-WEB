using System.Net;
using System.Text;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>Lot 7 : renommer une base pointeuse par l'API fait suivre les droits POINTEUSE:&lt;nom&gt;:… des comptes.</summary>
public class PointeuseDatabaseUpdateHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public PointeuseDatabaseUpdateHttpTests(ApiFactory factory)
    {
        _f = factory;
        _f.ResetStore();
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task UpdateAsync_Rename_UserRightsFollowTheNewName()
    {
        // Arrange
        // Base pointeuse MODIF_PTG (3) ; un compte a le droit de la lire.
        _f.Store.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 10052, PermissionCode = "POINTEUSE:MODIF_PTG:READ", IsGranted = true });
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/pointeuse-databases/3")
        {
            Content = new StringContent(
                """{"id":3,"serveur":"invalid.host.test","sqlAuth":true,"nomBd":"PTG_RENOMMEE","typeBase":"STANDARD"}""",
                Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiFactory.TokenFor("admin")}");

        // Act
        var reponse = await _http.SendAsync(req);

        // Assert
        reponse.StatusCode.Should().Be(HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync());
        var codes = _f.Store.Get<UserPermission>().Items.Where(p => p.IdResponsable == 10052).Select(p => p.PermissionCode).ToList();
        codes.Should().Contain("POINTEUSE:PTG_RENOMMEE:READ");
        codes.Should().NotContain(c => c.StartsWith("POINTEUSE:MODIF_PTG:"));
    }
}
