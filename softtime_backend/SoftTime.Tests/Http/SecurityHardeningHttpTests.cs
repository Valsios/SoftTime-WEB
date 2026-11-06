using System.Net;
using System.Text.Json;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Api.Middleware;
using SoftTime.Api.Security;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests.Http;

/// <summary>
/// Durcissement de l'API : messages d'erreur techniques jamais renvoyés au client, catalogue des routes réservé aux
/// comptes connectés, validation des entrées avant traitement, jeton de rafraîchissement en cookie HttpOnly.
/// </summary>
public class SecurityHardeningHttpTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http;

    public SecurityHardeningHttpTests(ApiFactory factory) => _http = factory.CreateClient();

    // ---------- Middleware d'exceptions ----------

    [Fact]
    public async Task Middleware_BusinessError_ReturnsItsMessageAs400()
    {
        // Act
        var (status, body) = await RunMiddleware(_ => throw new DomainException("Période déjà validée."));

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("Période déjà validée.", body);
    }

    public static TheoryData<DomainException, int> TypedExceptions => new()
    {
        { new NotFoundException("Salarié introuvable."), StatusCodes.Status404NotFound },
        { new UnauthorizedException("Session expirée."), StatusCodes.Status401Unauthorized },
        { new ForbiddenException("Droit « Bases » requis."), StatusCodes.Status403Forbidden },
        { new ConflictException("Login déjà utilisé."), StatusCodes.Status409Conflict }
    };

    [Theory]
    [MemberData(nameof(TypedExceptions))]
    public async Task Middleware_TypedException_ReturnsItsStatusAndMessage(DomainException exception, int expectedStatus)
    {
        // Act
        var (status, body) = await RunMiddleware(_ => throw exception);

        // Assert
        Assert.Equal(expectedStatus, status);
        Assert.Contains(exception.Message, body);
    }

    [Fact]
    public async Task Middleware_FieldValidationException_Returns400WithTheField()
    {
        // Act
        var (status, body) = await RunMiddleware(_ => throw new FieldValidationException("login", "Login obligatoire."));

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("login", json.RootElement.GetProperty("field").GetString());
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

    private static async Task<(int Status, string Body)> RunMiddleware(RequestDelegate next)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance).InvokeAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
