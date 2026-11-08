using System.Net;
using System.Text;
using System.Text.Json;

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
        status.Should().Be(StatusCodes.Status400BadRequest);
        body.Should().Contain("Période déjà validée.");
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
        status.Should().Be(expectedStatus);
        body.Should().Contain(exception.Message);
    }

    [Fact]
    public async Task Middleware_FieldValidationException_Returns400WithTheField()
    {
        // Act
        var (status, body) = await RunMiddleware(_ => throw new FieldValidationException("login", "Login obligatoire."));

        // Assert
        status.Should().Be(StatusCodes.Status400BadRequest);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("field").GetString().Should().Be("login");
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
        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.Should().Contain("Erreur interne.");
        body.Should().NotContain("Sequence");
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
        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.Should().NotContain("secret");
    }

    // ---------- Catalogue des routes ----------

    [Fact]
    public async Task MetaEndpoints_Anonymous_Returns401()
    {
        // Act
        var response = await _http.GetAsync("/api/v1/meta/endpoints");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- Validation des entrées ----------

    [Fact]
    public async Task Login_OversizedLogin_Returns400OnTheLoginField()
    {
        // Arrange
        var body = JsonSerializer.Serialize(new { login = new string('a', 500), password = "FAKE-Pwd-123!" });

        // Act
        var response = await _http.PostAsync("/api/v1/auth/login", new StringContent(body, Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("field").GetString().Should().Be("login");
    }

    [Fact]
    public async Task Login_EmptyCredentials_StaysAnOrdinaryLoginFailure()
    {
        // Act
        var response = await _http.PostAsync("/api/v1/auth/login",
            new StringContent("""{"login":"","password":""}""", Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("""{"serveur":"srv","nomBd":"base","typeBase":"INCONNU"}""", "typeBase")]
    [InlineData("""{"serveur":"srv\u0000;Password=x","nomBd":"base","typeBase":"STANDARD"}""", "serveur")]
    public void SageDbDtoValidator_UnknownTypeOrControlCharacters_IsInvalid(string json, string field)
    {
        // Arrange
        var dto = JsonSerializer.Deserialize<SageDbDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // Act
        var result = new SoftTime.Application.Validators.SageDbDtoValidator().Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        (char.ToLowerInvariant(result.Errors[0].PropertyName[0]) + result.Errors[0].PropertyName[1..]).Should().Be(field);
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
        sent.RefreshToken.Should().BeNull();
        sent.Token.Should().Be("ACCESS");
        var cookie = context.Response.Headers.SetCookie.ToString();
        cookie.Should().StartWith($"{RefreshTokenCookie.Name}=FAKE-REFRESH");
        cookie.Should().ContainEquivalentOf("httponly");
        cookie.Should().ContainEquivalentOf("samesite=strict");
        cookie.Should().ContainEquivalentOf("path=/");
    }

    [Fact]
    public void Read_CookieAndBody_PrefersTheCookie()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{RefreshTokenCookie.Name}=FROM-COOKIE";

        // Act + Assert
        RefreshTokenCookie.Read(context, "FROM-BODY").Should().Be("FROM-COOKIE");
        RefreshTokenCookie.Read(new DefaultHttpContext(), "FROM-BODY").Should().Be("FROM-BODY");
    }

    [Fact]
    public async Task Logout_AuthenticatedClient_ClearsTheRefreshCookie()
    {
        // Act
        var response = await _http.PostAsync("/api/v1/auth/logout", new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cookie = response.Headers.GetValues("Set-Cookie").Should().ContainSingle().Which;
        cookie.Should().StartWith($"{RefreshTokenCookie.Name}=;");
    }

    // ---------- Journalisation ----------

    [Theory]
    [InlineData("agent", "agent")]
    [InlineData("faux\r\nINFO connexion réussie", "fauxINFO connexion réussie")]
    public void LogSafe_ControlCharacters_AreRemoved(string typed, string expected)
    {
        // Act + Assert
        AuthenticationService.LogSafe(typed).Should().Be(expected);
    }

    [Fact]
    public void LogSafe_LongValue_IsTruncated()
    {
        // Act
        var logged = AuthenticationService.LogSafe(new string('x', 100));

        // Assert
        logged.Length.Should().Be(33);
        logged.Should().EndWith("…");
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
