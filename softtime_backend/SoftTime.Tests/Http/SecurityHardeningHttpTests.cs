using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Api.Middleware;
using SoftTime.Api.Security;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;

namespace SoftTime.Tests.Http;

/// <summary>
/// Durcissement de l'API : messages d'erreur techniques jamais renvoyés au client, catalogue des routes réservé aux
/// comptes connectés, validation des entrées avant traitement, jeton de rafraîchissement en cookie HttpOnly.
/// </summary>
public class SecurityHardeningHttpTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http;

    public SecurityHardeningHttpTests(ApiFactory factory) => _http = factory.CreateClient();

    // ---------- Origine des exceptions ----------

    [Fact]
    public void IsApplication_ExceptionThrownBySoftTimeCode_IsTrue()
    {
        // Arrange
        var ex = Catch(() => throw new InvalidOperationException("Règle métier."));

        // Act
        var result = ExceptionOrigin.IsApplication(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsApplication_ExceptionThrownByTheFramework_IsFalse()
    {
        // Arrange : levées par System.Linq et System.Collections.Generic, pas par SoftTime.
        var linq = Catch(() => Enumerable.Empty<int>().First());
        var dictionary = Catch(() => _ = new Dictionary<string, int>()["Password=secret"]);

        // Act + Assert
        Assert.IsType<InvalidOperationException>(linq);
        Assert.False(ExceptionOrigin.IsApplication(linq));
        Assert.IsType<KeyNotFoundException>(dictionary);
        Assert.False(ExceptionOrigin.IsApplication(dictionary));
    }

    // ---------- Middleware d'exceptions ----------

    [Fact]
    public async Task Middleware_BusinessError_ReturnsItsMessageAs400()
    {
        // Act
        var (status, body) = await RunMiddleware(_ => throw new InvalidOperationException("Période déjà validée."));

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("Période déjà validée.", body);
    }

    [Fact]
    public async Task Middleware_FrameworkInvalidOperation_Returns500WithoutDetail()
    {
        // Act : même type qu'une erreur métier, mais levé par .NET (comme EF Core ou SqlClient le feraient).
        var (status, body) = await RunMiddleware(ctx =>
        {
            Enumerable.Empty<int>().First();
            return Task.CompletedTask;
        });

        // Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Contains("Erreur interne.", body);
        Assert.DoesNotContain("Sequence", body);
    }

    [Fact]
    public async Task Middleware_FrameworkKeyNotFound_DoesNotEchoTheKey()
    {
        // Act
        var (status, body) = await RunMiddleware(ctx =>
        {
            var unused = new Dictionary<string, int>()["Password=secret"];
            return Task.CompletedTask;
        });

        // Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("secret", body);
    }

    // ---------- Catalogue des routes ----------

    [Fact]
    public async Task MetaEndpoints_Anonymous_Returns401()
    {
        // Act
        var response = await _http.GetAsync("/api/meta/endpoints");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Validation des entrées ----------

    [Fact]
    public async Task Login_OversizedLogin_Returns400OnTheLoginField()
    {
        // Arrange
        var body = JsonSerializer.Serialize(new { login = new string('a', 500), password = "FAKE-Pwd-123!" });

        // Act
        var response = await _http.PostAsync("/api/auth/login", new StringContent(body, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("login", json.RootElement.GetProperty("field").GetString());
    }

    [Fact]
    public async Task Login_EmptyCredentials_StaysAnOrdinaryLoginFailure()
    {
        // Act
        var response = await _http.PostAsync("/api/auth/login",
            new StringContent("""{"login":"","password":""}""", Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"serveur":"srv","nomBd":"base","typeBase":"INCONNU"}""", "typeBase")]
    [InlineData("""{"serveur":"srv\u0000;Password=x","nomBd":"base","typeBase":"STANDARD"}""", "serveur")]
    public void SageDbValidator_RejectsUnknownTypeOrControlCharacters(string json, string field)
    {
        // Arrange
        var dto = JsonSerializer.Deserialize<SageDbDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // Act
        var result = new SoftTime.Application.Validators.SageDbDtoValidator().Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(field, char.ToLowerInvariant(result.Errors[0].PropertyName[0]) + result.Errors[0].PropertyName[1..]);
    }

    // ---------- Cookie du jeton de rafraîchissement ----------

    [Fact]
    public void Issue_SetsHttpOnlyStrictCookie_AndRemovesTokenFromBody()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var response = new LoginResponse("ACCESS", 1, "agent", "Agent", 2, null, Array.Empty<int>(), Array.Empty<SageDbDto>(), RefreshToken: "FAKE-REFRESH");

        // Act
        var sent = RefreshTokenCookie.Issue(context, response, new AccountSecurityOptions { RefreshTokenDays = 7 });

        // Assert
        Assert.Null(sent.RefreshToken);
        Assert.Equal("ACCESS", sent.Token);
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.StartsWith($"{RefreshTokenCookie.Name}=FAKE-REFRESH", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_PrefersTheCookieOverTheBody()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{RefreshTokenCookie.Name}=FROM-COOKIE";

        // Act + Assert
        Assert.Equal("FROM-COOKIE", RefreshTokenCookie.Read(context, "FROM-BODY"));
        Assert.Equal("FROM-BODY", RefreshTokenCookie.Read(new DefaultHttpContext(), "FROM-BODY"));
    }

    [Fact]
    public async Task Logout_ClearsTheRefreshCookie()
    {
        // Act
        var response = await _http.PostAsync("/api/auth/logout", new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{RefreshTokenCookie.Name}=;", cookie);
    }

    // ---------- Journalisation ----------

    [Theory]
    [InlineData("agent", "agent")]
    [InlineData("faux\r\nINFO connexion réussie", "fauxINFO connexion réussie")]
    public void LogSafe_RemovesControlCharacters(string typed, string expected)
    {
        // Act + Assert
        Assert.Equal(expected, AuthService.LogSafe(typed));
    }

    [Fact]
    public void LogSafe_TruncatesLongValues()
    {
        // Act
        var logged = AuthService.LogSafe(new string('x', 100));

        // Assert
        Assert.Equal(33, logged.Length);
        Assert.EndsWith("…", logged);
    }

    private static Exception Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }
        throw new InvalidOperationException("Aucune exception levée.");
    }

    private static async Task<(int Status, string Body)> RunMiddleware(RequestDelegate next)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance).InvokeAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
