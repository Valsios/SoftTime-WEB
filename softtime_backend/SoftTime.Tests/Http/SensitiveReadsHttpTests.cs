using System.Net;

namespace SoftTime.Tests.Http;

/// <summary>
/// Étape 4b-9 : les lectures sensibles de la section Paramètres exigent la porte 2 ET une permission fine
/// (administrateur toujours accepté). Aucun écran hors Paramètres n'appelle ces routes.
/// </summary>
public class SensitiveReadsHttpTests : IClassFixture<ApiFactory>
{
    private const string Denied = "Vous n'avez pas la permission requise pour cette consultation.";

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public SensitiveReadsHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    private const string U = "usersreader", R = "rolesreader", P = "privreader", D = "dbreader";

    // Route → profils (porte 2 + bonne permission) qui y accèdent. Tous les autres comptes à porte 2 sont refusés.
    private static readonly Dictionary<string, (string Url, string[] Allowed)> Routes = new()
    {
        ["GET users"] = ("/api/v1/users", new[] { U, P, D }),
        ["GET users/paged"] = ("/api/v1/users/paged", new[] { U }),
        ["GET users/{id}"] = ("/api/v1/users/10052", new[] { U }),
        ["GET roles"] = ("/api/v1/roles", new[] { U, P }),
        ["GET roles/paged"] = ("/api/v1/roles/paged", new[] { R }),
        ["GET privileges/droits"] = ("/api/v1/privileges/droits", new[] { P, R }),
        ["GET privileges?roleId"] = ("/api/v1/privileges?roleId=91", new[] { P, R }),
        ["GET privileges/user/{id}"] = ("/api/v1/privileges/user/10052", new[] { P }),
        ["GET privileges/role/{id}"] = ("/api/v1/privileges/role/91", new[] { P }),
        ["GET db-access"] = ("/api/v1/db-access", new[] { D }),
    };

    private static readonly string[] Gate2Profiles = { U, R, P, D, "paramsother", "param2" };

    public static IEnumerable<object[]> Cases()
        => Routes.Keys.SelectMany(r => Gate2Profiles.Concat(new[] { "admin", "joel", "user1", "anonyme" }).Select(p => new object[] { r, p }));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Sensitive_read_is_allowed_only_with_gate_2_and_a_matching_permission(string route, string profile)
    {
        var (url, allowed) = Routes[route];
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, url));
        var text = await resp.Content.ReadAsStringAsync();
        ApiFactory.Report($"4B9|{route}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text.Length > 90 ? text[..90] : text)}");

        if (profile == "anonyme")
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        else if (profile == "admin" || allowed.Contains(profile))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        else
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            // joel et user1 n'ont pas la porte 2 : RequireDroit répond sans corps ; les autres reçoivent le message prévu.
            if (Gate2Profiles.Contains(profile)) text.Should().Contain(Denied);
        }
    }

    [Fact]
    public async Task Own_detailed_permissions_stay_readable_through_the_login_response_not_this_route()
    {
        // GET privileges/user/{id} est réservé à PARAMS:PRIVILEGES ; un compte lit les siens via /auth/me.
        var me = await _http.SendAsync(_f.Request("param2", HttpMethod.Get, "/api/v1/auth/me"));
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var route = await _http.SendAsync(_f.Request("param2", HttpMethod.Get, "/api/v1/privileges/user/10040"));
        route.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Routes volontairement NON protégées par cette étape : elles documentent le comportement actuel.
    [Theory]
    [InlineData("/api/v1/privileges/audit", "admin")]            // porte Traçabilité (5), pas Paramètres
    public async Task Audit_of_rights_is_governed_by_the_traceability_gate(string url, string profile)
    {
        var ok = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, url));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var refused = await _http.SendAsync(_f.Request(U, HttpMethod.Get, url));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);   // porte 2 et PARAMS:USERS ne donnent pas la porte 5
    }

    [Theory]
    [InlineData("/api/v1/sage-databases")]
    [InlineData("/api/v1/pointeuse-databases")]
    [InlineData("/api/v1/field-roles")]
    public async Task Lists_used_by_other_screens_stay_open_to_any_authenticated_account(string url)
    {
        var resp = await _http.SendAsync(_f.Request("paramsother", HttpMethod.Get, url));
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
