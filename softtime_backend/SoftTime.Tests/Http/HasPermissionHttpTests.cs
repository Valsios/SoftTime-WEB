using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using SoftTime.Api.Authorization;
using SoftTime.Api.Middleware;
using SoftTime.Api.Security;
using SoftTime.Application.Abstractions.Permissions;

namespace SoftTime.Tests.Http;

/// <summary>
/// Lot 1 : les permissions fines sont appliquées par le serveur ([HasPermission]) et un jeton dont les droits ont
/// changé est refusé (401) jusqu'à son renouvellement.
/// </summary>
public class HasPermissionHttpTests : IClassFixture<ApiFactory>
{
    private const decimal ParamsOtherId = 10074;
    private const string PerDatabase = "droit par base : IDatabaseConfigurationAuthorization";
    private const string ReadOnlyPost = "lecture ou mise en forme (POST pour le corps), n'écrit rien";

    /// <summary>
    /// Écritures sans [HasPermission] ni [RequireAdmin] : le contrôle est ailleurs (justifié). Toute nouvelle écriture
    /// sans permission fait échouer <see cref="Writes_EveryAction_RequiresPermissionOrAdministrator"/>.
    /// </summary>
    private static readonly Dictionary<string, string> s_writesCheckedElsewhere = new(StringComparer.OrdinalIgnoreCase)
    {
        ["POST api/v1/excel/export"] = ReadOnlyPost,
        ["POST api/v1/excel/export-workbook"] = ReadOnlyPost,
        ["POST api/v1/departement-service/lookup-batch"] = ReadOnlyPost,
        ["POST api/v1/sage-databases/lookup-batch"] = ReadOnlyPost,
        ["POST api/v1/discovery/databases"] = ReadOnlyPost,
        ["POST api/v1/discovery/tables"] = ReadOnlyPost,
        ["POST api/v1/discovery/columns"] = ReadOnlyPost,
        ["POST api/v1/sage-databases"] = PerDatabase,
        ["PUT api/v1/sage-databases/{id:int}"] = PerDatabase,
        ["DELETE api/v1/sage-databases/{id:int}"] = PerDatabase,
        ["POST api/v1/sage-databases/test-connection"] = PerDatabase,
        ["POST api/v1/sage-databases/activate"] = "choix de la base de travail : usage contrôlé par TenantConnectionService",
        ["POST api/v1/pointeuse-databases"] = PerDatabase,
        ["PUT api/v1/pointeuse-databases/{id:int}"] = PerDatabase,
        ["DELETE api/v1/pointeuse-databases/{id:int}"] = PerDatabase,
        ["POST api/v1/pointeuse-databases/test-connection"] = PerDatabase,
    };

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public HasPermissionHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    [Fact]
    public void Writes_EveryAction_RequiresPermissionOrAdministrator()
    {
        // Arrange
        var endpoints = _f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null
                        && e.Metadata.GetMetadata<IAllowAnonymous>() is null);

        // Act
        var unprotected = endpoints.SelectMany(UnprotectedWrites).ToList();

        // Assert
        unprotected.Should().BeEmpty("chaque écriture doit porter [HasPermission] ou [RequireAdmin], ou être justifiée");
    }

    [Theory]
    [InlineData("DELETE", "/api/v1/categories/1")]
    [InlineData("POST", "/api/v1/categories")]
    [InlineData("POST", "/api/v1/tolerances")]
    [InlineData("GET", "/api/v1/holidays")]
    [InlineData("PUT", "/api/v1/clock-params")]
    public async Task Action_AccountWithGateButWithoutPermission_Gets403(string method, string url)
    {
        // Arrange : porte Paramètres, mais categories.read (lecture) pour seule permission.
        var before = _f.Snapshot();
        var request = _f.Request("paramsother", new HttpMethod(method), url, method == "GET" ? null : "{}");

        // Act
        var response = await _http.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _f.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("paramsother")]
    [InlineData("admin")]
    public async Task Action_AccountWithPermissionOrAdministrator_IsNotRefused(string profile)
    {
        // Arrange
        var request = _f.Request(profile, HttpMethod.Get, "/api/v1/categories");

        // Act
        var response = await _http.SendAsync(request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("privreader", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("paramsother", HttpStatusCode.Forbidden)]
    public async Task Catalog_ReservedToPrivilegesReaders(string profile, HttpStatusCode expected)
    {
        // Arrange
        var request = _f.Request(profile, HttpMethod.Get, "/api/v1/privileges/catalog");

        // Act
        var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK) text.Should().Contain("self.read");
    }

    [Theory]
    [InlineData("GET", "/api/v1/payroll-write-config")]
    [InlineData("POST", "/api/v1/payroll-write-config/test")]
    [InlineData("PUT", "/api/v1/privileges/role/91")]
    public async Task AdministratorOnlyRoutes_GateTwoAccount_Gets403(string method, string url)
    {
        // Arrange
        var before = _f.Snapshot();
        var request = _f.Request("privreader", new HttpMethod(method), url, method == "GET" ? null : "{\"grantedPermissions\":[]}");

        // Act
        var response = await _http.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _f.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("0000000000000000")]
    [InlineData("")]
    public async Task Token_WithOutdatedStamp_Gets401UntilRenewed(string stamp)
    {
        // Arrange
        var request = WithToken(HttpMethod.Get, "/api/v1/categories", await TokenAsync(ParamsOtherId, 91, stamp));

        // Act
        var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        text.Should().Contain(TokenFreshnessMiddleware.StaleTokenMessage);
    }

    [Fact]
    public async Task Token_AfterRightsChange_IsRefusedOnTheNextRequest()
    {
        // Arrange : jeton à jour, accepté ; puis l'administrateur retire tous les droits du compte.
        const decimal userId = 10081;
        var token = await TokenAsync(userId, 91, stamp: null);
        var accepted = await _http.SendAsync(WithToken(HttpMethod.Get, "/api/v1/auth/me", token));
        var change = await _http.SendAsync(
            _f.Request("admin", HttpMethod.Put, $"/api/v1/privileges/user/{(int)userId}", "{\"grantedPermissions\":[]}"));

        // Act
        var refused = await _http.SendAsync(WithToken(HttpMethod.Get, "/api/v1/auth/me", token));

        // Assert
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static IEnumerable<string> UnprotectedWrites(RouteEndpoint endpoint)
    {
        var guarded = endpoint.Metadata.GetMetadata<HasPermissionAttribute>() is not null
                      || endpoint.Metadata.GetMetadata<RequireAdminAttribute>() is not null;
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>();
        return methods
            .Where(method => method is "POST" or "PUT" or "PATCH" or "DELETE")
            .Select(method => $"{method} {endpoint.RoutePattern.RawText?.TrimStart('/')}")
            .Where(key => !guarded && !s_writesCheckedElsewhere.ContainsKey(key));
    }

    /// <summary>Jeton portant l'empreinte donnée, ou (null) l'empreinte et les permissions actuelles du compte.</summary>
    private async Task<string> TokenAsync(decimal userId, decimal roleId, string? stamp)
    {
        using var scope = _f.Services.CreateScope();
        var rights = await scope.ServiceProvider.GetRequiredService<IEffectiveRightsService>().GetAsync(userId, fresh: true);
        var id = ((long)userId).ToString();
        var claims = new List<Claim>
        {
            new("uid", id),
            new(ClaimTypes.Name, $"test{id}"),
            new("roleId", ((long)roleId).ToString()),
            new("rights", string.Join(",", rights.Gates)),
            new(PermissionClaims.Permissions, PermissionClaims.Join(rights.TokenPermissions)),
            new(PermissionClaims.RightsStamp, stamp ?? rights.Stamp)
        };
        return ApiFactory.Mint(claims, DateTime.UtcNow.AddHours(1));
    }

    private static HttpRequestMessage WithToken(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return request;
    }
}
