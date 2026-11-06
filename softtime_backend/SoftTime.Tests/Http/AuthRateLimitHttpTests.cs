using System.Net;

namespace SoftTime.Tests.Http;

/// <summary>Limitation des endpoints d'authentification : classe isolée, elle épuise le quota de sa propre fabrique.</summary>
public class AuthRateLimitHttpTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http;

    public AuthRateLimitHttpTests(ApiFactory factory) => _http = factory.CreateClient();

    [Fact]
    public async Task Refresh_BeyondPerMinuteLimit_ReturnsTooManyRequests()
    {
        // Arrange : la limite par défaut est de 20 requêtes par minute et par adresse.
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < 25; i++)
            statuses.Add((await _http.PostAsync("/api/auth/refresh", SecurityPipelineHttpTests.Json("""{"refreshToken":"jeton-inconnu"}"""))).StatusCode);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
