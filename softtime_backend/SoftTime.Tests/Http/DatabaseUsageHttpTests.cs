using System.Net;

namespace SoftTime.Tests.Http;

/// <summary>
/// Étape 4b-5 : utiliser une base SAGE = administrateur, ou (porte 1 effective ET base cochée dans « Accès bases » ET lecture).
/// Pointeuse = administrateur, ou (porte 1 effective ET POINTEUSE:&lt;base&gt;:READ). Un refus est un 403, jamais un 401.
/// </summary>
public class DatabaseUsageHttpTests : IClassFixture<ApiFactory>
{
    private const string Denied = "Vous n'avez pas accès à cette base.";

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public DatabaseUsageHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    // Une route d'usage par famille : catalogue, traitements, activation (exemptée du middleware), pointeuse.
    private static readonly Dictionary<string, (string Method, string Url, string? Body, bool NeedsPointeuse)> Routes = new()
    {
        ["catalogue (GET cardpaie)"] = ("GET", "/api/v1/card-paie", null, false),
        ["traitements (GET shifts)"] = ("GET", "/api/v1/shifts", null, false),
        ["activation (POST sage-databases/activate)"] = ("POST", "/api/v1/sage-databases/activate", null, false),
        ["pointeuse (POST punches/import-clock)"] = ("POST", "/api/v1/punches/import-clock", """{"matriculeFrom":null,"matriculeTo":null,"from":"2026-01-01T00:00:00","to":"2026-01-02T00:00:00"}""", true),
    };

    public static IEnumerable<object[]> Names(params string[] profiles)
        => profiles.SelectMany(p => Routes.Keys.Select(r => new object[] { p, r }));

    public static IEnumerable<object[]> AllowedCases()
        => Names("admin", "joel");

    public static IEnumerable<object[]> DeniedCases()
        => Names("nolink", "noread", "nogate");

    private async Task<(HttpResponseMessage Resp, string Body)> Send(string profile, string route, bool withPointeuse = true, bool withSage = true)
    {
        var (method, url, body, needsPointeuse) = Routes[route];
        var req = _f.Request(profile, new HttpMethod(method), url, body,
            withSage ? ApiFactory.SageName : null, needsPointeuse && withPointeuse ? "PTG" : null);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        ApiFactory.Report($"4B5|{route}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text.Length > 100 ? text[..100] : text)}");
        return (resp, text);
    }

    [Theory]
    [MemberData(nameof(AllowedCases))]
    public async Task Administrator_and_a_checked_account_with_read_use_the_base(string profile, string route)
    {
        var (resp, _) = await Send(profile, route);
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [MemberData(nameof(DeniedCases))]
    public async Task Unchecked_base_missing_read_or_no_gate_is_refused_with_403_and_the_message(string profile, string route)
    {
        var (resp, text) = await Send(profile, route);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains(Denied, text);
    }

    [Theory]
    [InlineData("traitements (GET shifts)")]
    [InlineData("catalogue (GET cardpaie)")]
    [InlineData("activation (POST sage-databases/activate)")]
    public async Task Account_like_user1_without_any_base_gets_403_not_401(string route)
    {
        var (resp, _) = await Send("user1", route);
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Account_like_user1_without_the_sage_header_still_gets_400()
    {
        var (resp, text) = await Send("user1", "catalogue (GET cardpaie)", withSage: false);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("X-Sage-Database", text);
    }

    [Fact]
    public async Task Pointeuse_is_allowed_with_read_and_refused_without()
    {
        var (allowed, _) = await Send("joel", "pointeuse (POST punches/import-clock)");
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);

        // Même base SAGE autorisée, mais aucun droit sur la pointeuse.
        var (sageOnly, _) = await Send("ptenone", "catalogue (GET cardpaie)");
        Assert.NotEqual(HttpStatusCode.Forbidden, sageOnly.StatusCode);
        var (refused, text) = await Send("ptenone", "pointeuse (POST punches/import-clock)");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains(Denied, text);
    }

    [Fact]
    public async Task Stale_pointeuse_header_does_not_break_routes_that_do_not_use_the_pointeuse()
    {
        var req = _f.Request("ptenone", HttpMethod.Get, "/api/v1/card-paie", null, ApiFactory.SageName, "PTG");
        var resp = await _http.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Login_and_me_propose_only_the_bases_the_account_can_use()
    {
        // R4 : la liste proposée est exactement celle que le serveur accepte.
        foreach (var (profile, expected) in new[] { ("joel", 1), ("nolink", 0), ("noread", 0), ("nogate", 0), ("ptenone", 1) })
        {
            var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, "/api/v1/auth/me"));
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var count = doc.RootElement.GetProperty("data").GetProperty("authorizedDatabases").GetArrayLength();
            Assert.True(expected == count, $"{profile} : {count} base(s) proposée(s), {expected} attendue(s)");
        }
    }
}
