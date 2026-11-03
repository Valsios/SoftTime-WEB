using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Vérification HTTP de l'étape 3A : hôte en mémoire, jetons de test, aucune base réelle.
/// Les observations détaillées (statut, format de corps) sont écrites dans <see cref="ApiFactory.ReportPath"/>.
/// </summary>
public class ApiHttpSecurityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public ApiHttpSecurityTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    private const string SageBody = """{"id":0,"serveur":"h","login":"l","password":"FAKE-SAGE-SECRET-PWD","sqlAuth":true,"nomBd":"NEWBASE","typeBase":"STANDARD"}""";
    private const string SageUpdateBody = """{"id":3036,"serveur":"h","login":"l","password":"p","sqlAuth":true,"nomBd":"MODIF_SAGE_2","typeBase":"STANDARD"}""";
    private const string PointeuseUpdateBody = """{"id":3,"serveur":"h","login":"l","password":"p","sqlAuth":true,"nomBd":"MODIF_PTG_2","typePointage":null,"active":false,"typeBase":"STANDARD"}""";
    private const string PointeuseBody = """{"id":0,"serveur":"h","login":"l","password":"p","sqlAuth":true,"nomBd":"NEWPTG","typePointage":null,"active":false,"typeBase":"STANDARD"}""";
    private const string TestConnBody = """{"serveur":"h","nomBd":"X","sqlAuth":true,"login":"l","password":"p","typeBase":"STANDARD"}""";

    // Kind : admin = [RequireAdmin] ; db = contrôle de service par base ; dbany = test de connexion / découverte.
    private static readonly Dictionary<string, (string Method, string Url, string? Body, string Kind)> Routes = new()
    {
        ["PUT privileges/{roleId}"] = ("PUT", "/api/privileges/91", "[1]", "admin"),
        ["PUT privileges/user/{id}"] = ("PUT", "/api/privileges/user/10050", """{"grantedPermissions":["PROC:SHIFTS"]}""", "admin"),
        ["POST privileges/user/{id}/reset"] = ("POST", "/api/privileges/user/10050/reset", null, "admin"),
        ["PUT db-access/{userId}"] = ("PUT", "/api/db-access/10050", "[3033]", "admin"),
        ["POST roles"] = ("POST", "/api/roles", """{"id":0,"nom":"NouveauRole"}""", "admin"),
        ["PUT roles/{id}"] = ("PUT", "/api/roles/91", """{"id":91,"nom":"CibleRenomme"}""", "admin"),
        ["DELETE roles/{id}"] = ("DELETE", "/api/roles/90", null, "admin"),
        ["POST sage-databases"] = ("POST", "/api/sage-databases", SageBody, "db"),
        ["PUT sage-databases/{id}"] = ("PUT", "/api/sage-databases/3036", SageUpdateBody, "db"),
        ["DELETE sage-databases/{id}"] = ("DELETE", "/api/sage-databases/3035", null, "db"),
        ["POST pointeuse-databases"] = ("POST", "/api/pointeuse-databases", PointeuseBody, "db"),
        ["PUT pointeuse-databases/{id}"] = ("PUT", "/api/pointeuse-databases/3", PointeuseUpdateBody, "db"),
        ["DELETE pointeuse-databases/{id}"] = ("DELETE", "/api/pointeuse-databases/2", null, "db"),
        ["POST sage-databases/test-connection"] = ("POST", "/api/sage-databases/test-connection", TestConnBody, "dbany"),
        ["POST pointeuse-databases/test-connection"] = ("POST", "/api/pointeuse-databases/test-connection", TestConnBody, "dbany"),
        ["GET discovery/servers"] = ("GET", "/api/discovery/servers", null, "dbany"),
        ["POST discovery/databases"] = ("POST", "/api/discovery/databases", """{"serveur":"h","sqlAuth":true,"login":"l","password":"p"}""", "dbany"),
        ["POST discovery/tables"] = ("POST", "/api/discovery/tables", """{"serveur":"h","base":"B","sqlAuth":true,"login":"l","password":"p"}""", "dbany"),
        ["POST discovery/columns"] = ("POST", "/api/discovery/columns", """{"serveur":"h","base":"B","table":"T","sqlAuth":true,"login":"l","password":"p"}""", "dbany"),
    };

    public static IEnumerable<object[]> RefusedCases()
    {
        foreach (var route in Routes.Keys)
            foreach (var profile in new[] { "anonyme", "joel", "user1", "param2" })
                yield return new object[] { route, profile };
    }

    public static IEnumerable<object[]> AdminCases() => Routes.Keys.Select(r => new object[] { r });

    private static string Format(HttpResponseMessage r, string body)
    {
        if (body.Length == 0) return "corps vide";
        var type = r.Content.Headers.ContentType?.MediaType ?? "?";
        if (type.Contains("json") && body.TrimStart().StartsWith("{") && body.Contains("\"error\"")) return "json {error}";
        if (type.Contains("json")) return "json autre";
        return $"texte ({type})";
    }

    private async Task<(HttpResponseMessage Resp, string Body)> Send(string profile, string method, string url, string? body, string? sage = null)
    {
        var resp = await _http.SendAsync(_f.Request(profile, new HttpMethod(method), url, body, sage));
        return (resp, await resp.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public async Task Refused_profiles_get_401_or_403_and_nothing_is_written(string route, string profile)
    {
        var (method, url, body, kind) = Routes[route];
        var before = _f.Snapshot();
        var (resp, text) = await Send(profile, method, url, body);
        var after = _f.Snapshot();

        ApiFactory.Report($"V2|{route}|{profile}|{(int)resp.StatusCode}|{Format(resp, text)}|{Trunc(text)}");
        Assert.Equal(profile == "anonyme" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(before, after);

        if (profile == "param2" && kind == "admin")
            Assert.Contains(AdminAccess.ReservedMessage, text);
        if (profile == "joel" && kind == "db")
            Assert.Contains("permission", text);
        if (profile == "joel" && kind == "dbany")
            Assert.Contains("permission de configurer", text);
    }

    [Theory]
    [MemberData(nameof(AdminCases))]
    public async Task Admin_is_not_refused(string route)
    {
        var (method, url, body, _) = Routes[route];
        var (resp, text) = await Send("admin", method, url, body);
        ApiFactory.Report($"V2|{route}|admin|{(int)resp.StatusCode}|{Format(resp, text)}|{Trunc(text)}");
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    #region V3 : routes d'usage

    private static readonly (string Name, string Method, string Url, string? Body, bool NeedsTenant)[] UsageRoutes =
    {
        ("activate", "POST", "/api/sage-databases/activate", null, true),
        ("lookup", "GET", "/api/sage-databases/lookup/1", null, true),
        ("lookup-batch", "POST", "/api/sage-databases/lookup-batch", """["1"]""", true),
        ("departement-service-options", "GET", "/api/sage-databases/departement-service-options", null, true),
        ("cardpaie GET", "GET", "/api/cardpaie", null, true),
        ("sage-databases GET", "GET", "/api/sage-databases", null, false),
        ("sage-databases GET paged", "GET", "/api/sage-databases/paged", null, false),
        ("pointeuse-databases GET", "GET", "/api/pointeuse-databases", null, false),
        ("pointeuse-databases GET paged", "GET", "/api/pointeuse-databases/paged", null, false),
    };

    public static IEnumerable<object[]> UsageNames() => new[] { "activate", "lookup", "lookup-batch", "departement-service-options", "cardpaie GET",
        "sage-databases GET", "sage-databases GET paged", "pointeuse-databases GET", "pointeuse-databases GET paged" }.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(UsageNames))]
    public async Task Usage_routes_are_open_to_joel(string name)
    {
        var r = UsageRoutes.Single(x => x.Name == name);
        var (resp, text) = await Send("joel", r.Method, r.Url, r.Body, r.NeedsTenant ? ApiFactory.SageName : null);
        ApiFactory.Report($"V3|{name}|joel|{(int)resp.StatusCode}|{Format(resp, text)}|{Trunc(text)}");
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [MemberData(nameof(UsageNames))]
    public async Task Usage_routes_without_sage_header_give_400_to_user1(string name)
    {
        var r = UsageRoutes.Single(x => x.Name == name);
        var (resp, text) = await Send("user1", r.Method, r.Url, r.Body);
        ApiFactory.Report($"V3|{name}|user1 sans en-tête|{(int)resp.StatusCode}|{Format(resp, text)}|{Trunc(text)}");
        if (!r.NeedsTenant)
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
            return;
        }
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("X-Sage-Database", text);
    }

    [Theory]
    [InlineData("param2")]
    [InlineData("user1")]
    public async Task Activate_by_account_without_access_to_the_base_gives_403_with_message(string profile)
    {
        var (resp, text) = await Send(profile, "POST", "/api/sage-databases/activate", null, ApiFactory.SageName);
        ApiFactory.Report($"V3|activate sans accès|{profile}|{(int)resp.StatusCode}|{Format(resp, text)}|{Trunc(text)}");
        // Étape 4b-5 : un refus d'usage est un 403 (un 401 déconnectait le client). Avant : 401 « Base SAGE non autorisée pour cet utilisateur. »
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("Vous n'avez pas accès à cette base.", text);
    }

    /// <summary>
    /// Écart connu fermé à l'étape 4b-5 : avec la porte 1, activate refuse désormais une base absente de T_BDD_AUTORISE
    /// (avant : 200, la porte 1 contournait T_BDD_AUTORISE).
    /// </summary>
    [Fact]
    public async Task FormerGap_Activate_with_gate_1_refuses_a_base_missing_from_BDD_AUTORISE()
    {
        Assert.DoesNotContain(_f.Store.GetRepo<T_BDD_AUTORISE>().Items, a => a.IDRESPONSABLE == 10034 && a.IDBDD == 3034);
        var (resp, text) = await Send("joel", "POST", "/api/sage-databases/activate", null, ApiFactory.OtherSageName);
        ApiFactory.Report($"V3|activate base non attribuée|joel|{(int)resp.StatusCode}|{Format(resp, text)}|{Trunc(text)}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("Vous n'avez pas accès à cette base.", text);
        Assert.DoesNotContain(_f.Store.GetRepo<T_BDD_AUTORISE>().Items, a => a.IDRESPONSABLE == 10034 && a.IDBDD == 3034);
    }


    #endregion

    #region V5 : endpoints sans garde

    [Fact]
    public void V5_Endpoints_without_Authorize_in_touched_controllers_are_only_the_anonymous_auth_routes()
    {
        var actions = _f.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Endpoint: e, Action: e.Metadata.GetMetadata<ControllerActionDescriptor>()))
            .Where(x => x.Action is not null)
            .ToList();

        var touched = new[] { "AuthController", "UsersController", "RolesController", "PrivilegesController", "AuditDroitController", "DbAccessController",
            "DiscoveryController", "SageDatabasesController", "PointeuseDatabasesController", "CardPaieController", "PayrollWriteConfigurationController" };

        var anonymous = new List<string>();
        var authorizeOnly = new List<string>();
        foreach (var (endpoint, action) in actions)
        {
            var controller = action!.ControllerName + "Controller";
            if (!touched.Contains(controller)) continue;
            var label = $"{controller}.{action.ActionName} [{string.Join(",", action.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods) ?? Array.Empty<string>())} {endpoint.RoutePattern.RawText}]";
            var isAuthorized = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;
            var isAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (!isAuthorized || isAnonymous) { anonymous.Add(label); continue; }
            var guarded = action.FilterDescriptors.Select(f => f.Filter)
                .OfType<Microsoft.AspNetCore.Mvc.TypeFilterAttribute>()
                .Any(f => f.ImplementationType.Name is "RequireDroitFilter" or "RequireAdminFilter");
            if (!guarded) authorizeOnly.Add(label);
        }

        foreach (var a in anonymous) ApiFactory.Report($"V5|sans Authorize|{a}");
        foreach (var a in authorizeOnly) ApiFactory.Report($"V5|Authorize seul|{a}");

        // Connexion, rafraîchissement et déconnexion (sans jeton d'accès valide par nature), démarrage initial, politique.
        Assert.Equal(
            new[] { "CreateAdmin", "GetPasswordPolicy", "IsFirstRun", "Login", "Logout", "Refresh" },
            anonymous.Select(a => a.Split(' ')[0]).Select(a => a["AuthController.".Length..]).OrderBy(a => a, StringComparer.Ordinal).ToArray());
        Assert.All(anonymous, a => Assert.StartsWith("AuthController.", a));
    }

    #endregion

    #region V6 : mots de passe de base

    [Theory]
    [InlineData("admin", "/api/sage-databases")]
    [InlineData("joel", "/api/sage-databases")]
    [InlineData("user1", "/api/sage-databases")]
    [InlineData("joel", "/api/sage-databases/paged")]
    [InlineData("joel", "/api/pointeuse-databases")]
    [InlineData("joel", "/api/pointeuse-databases/paged")]
    [InlineData("joel", "/api/auth/me")]
    [InlineData("admin", "/api/auth/me")]
    public async Task V6_Responses_never_contain_a_database_password(string profile, string url)
    {
        var (resp, text) = await Send(profile, "GET", url, null);
        var leaks = new[] { ApiFactory.FakeSagePassword, ApiFactory.FakePointeusePassword }.Where(text.Contains).ToList();
        ApiFactory.Report($"V6|{profile}|GET {url}|{(int)resp.StatusCode}|fuite={(leaks.Count > 0 ? string.Join('+', leaks) : "non")}");
        Assert.Empty(leaks);
    }

    [Fact]
    public async Task V6_Create_and_update_responses_never_contain_the_submitted_password()
    {
        var (resp, text) = await Send("admin", "POST", "/api/sage-databases", SageBody.Replace("NEWBASE", "V6BASE"));
        ApiFactory.Report($"V6|admin|POST /api/sage-databases|{(int)resp.StatusCode}|fuite={(text.Contains(ApiFactory.FakeSagePassword) ? ApiFactory.FakeSagePassword : "non")}");
        Assert.True(resp.IsSuccessStatusCode);
        Assert.DoesNotContain(ApiFactory.FakeSagePassword, text);
    }

    #endregion

    private static string Trunc(string s) => s.Length <= 110 ? s.Replace('\n', ' ') : s[..110].Replace('\n', ' ') + "…";
}
