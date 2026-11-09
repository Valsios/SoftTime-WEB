using System.Net;

using SoftTime.Domain.Entities.SoftTime;

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
        // Lot 4a : l'import de pointages exige aussi PROC:PUNCHES ; ces tests portent sur l'usage de la base.
        lock (_f.Store)
        {
            var perms = _f.Store.Get<UserPermission>().Items;
            foreach (var id in new[] { 10034m, 10063m }.Where(id => !perms.Any(p => p.IdResponsable == id && p.PermissionCode == "PROC:PUNCHES")))
                perms.Add(new UserPermission { IdResponsable = id, PermissionCode = "PROC:PUNCHES", IsGranted = true });
        }
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
    public async Task SageRoutes_AdministratorOrCheckedAccountWithRead_UseTheBase(string profile, string route)
    {
        // Act
        var (resp, _) = await Send(profile, route);

        // Assert
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(DeniedCases))]
    public async Task SageRoutes_UncheckedBaseMissingReadOrGate_Return403WithMessage(string profile, string route)
    {
        // Act
        var (resp, text) = await Send(profile, route);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        text.Should().Contain(Denied);
    }

    [Theory]
    [InlineData("traitements (GET shifts)")]
    [InlineData("catalogue (GET cardpaie)")]
    [InlineData("activation (POST sage-databases/activate)")]
    public async Task SageRoutes_AccountWithoutAnyBase_Returns403(string route)
    {
        // Act
        var (resp, _) = await Send("user1", route);

        // Assert
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SageRoutes_WithoutSageHeader_Returns400()
    {
        // Act
        var (resp, text) = await Send("user1", "catalogue (GET cardpaie)", withSage: false);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        text.Should().Contain("X-Sage-Database");
    }

    [Fact]
    public async Task PointeuseRoutes_WithOrWithoutRead_AllowsOrRefuses()
    {
        // Act
        var (allowed, _) = await Send("joel", "pointeuse (POST punches/import-clock)");

        // Assert
        allowed.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        // Même base SAGE autorisée, mais aucun droit sur la pointeuse.
        var (sageOnly, _) = await Send("ptenone", "catalogue (GET cardpaie)");
        sageOnly.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        var (refused, text) = await Send("ptenone", "pointeuse (POST punches/import-clock)");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        text.Should().Contain(Denied);
    }

    [Fact]
    public async Task SageRoutes_StalePointeuseHeader_DoesNotBreak()
    {
        // Arrange
        var req = _f.Request("ptenone", HttpMethod.Get, "/api/v1/card-paie", null, ApiFactory.SageName, "PTG");

        // Act
        var resp = await _http.SendAsync(req);

        // Assert
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LoginAndMe_AnyAccount_ProposeOnlyUsableBases()
    {
        // R4 : la liste proposée est exactement celle que le serveur accepte.
        foreach (var (profile, expected) in new[] { ("joel", 1), ("nolink", 0), ("noread", 0), ("nogate", 0), ("ptenone", 1) })
        {
            var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, "/api/v1/auth/me"));
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var count = doc.RootElement.GetProperty("data").GetProperty("authorizedDatabases").GetArrayLength();
            (expected == count).Should().BeTrue($"{profile} : {count} base(s) proposée(s), {expected} attendue(s)");
        }
    }
}
