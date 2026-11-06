using System.Net;
using System.Text;

namespace SoftTime.Tests.Http;

/// <summary>
/// Pipeline HTTP de sécurité (convention §5.7 et §9.3) : en-têtes, CORS fermé aux origines inconnues,
/// limitation des endpoints d'authentification et endpoints de rafraîchissement anonymes.
/// Fabrique dédiée : la limitation compte les requêtes par instance d'application.
/// </summary>
public class SecurityPipelineHttpTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http;

    public SecurityPipelineHttpTests(ApiFactory factory) => _http = factory.CreateClient();

    [Fact]
    public async Task AnyResponse_Always_CarriesSecurityHeaders()
    {
        // Act
        var response = await _http.GetAsync("/health");

        // Assert
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Preflight_UnknownOrigin_IsNotAllowed()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        // Act
        var response = await _http.SendAsync(request);

        // Assert
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Swagger_OutsideDevelopment_IsNotExposed()
    {
        // Act
        var response = await _http.GetAsync("/swagger/v1/swagger.json");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Logout_UnknownToken_ReturnsNoContent()
    {
        // Act
        var response = await _http.PostAsync("/api/auth/logout", Json("""{"refreshToken":"jeton-inconnu"}"""));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    internal static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
