using System.Net;

namespace SoftTime.Tests.Http;

/// <summary>
/// REPORTS:PREDICTIONS : les lectures de l'atterrissage exigent la porte 6 ET la permission fine
/// (administrateur toujours accepté), avec le même message de refus que les lectures sensibles.
/// L'atterrissage étant calculé sur les pointages, la base SAGE et la pointeuse doivent être lisibles.
/// </summary>
public class PredictionsHttpTests : IClassFixture<ApiFactory>
{
    private const string Denied = "Vous n'avez pas la permission requise pour cette consultation.";

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public PredictionsHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    // L'atterrissage lit des données par personne : chaque route est contrôlée pour chaque profil.
    public static IEnumerable<object[]> Reads()
    {
        foreach (var url in new[] { "/api/v1/predictions/landing", "/api/v1/predictions/landing?week=2026-11-09", "/api/v1/predictions/landing/horizon" })
            foreach (var profile in new[] { "admin", "predreader", "prednobase", "prednopte", "gate6only", "user1", "param2", "joel", "anonyme" })
                yield return new object[] { url, profile };
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Landing_WithoutGate6OrPermission_IsRefused(string url, string profile)
    {
        // Act
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, url, sageHeader: ApiFactory.SageName));
        var text = await resp.Content.ReadAsStringAsync();
        ApiFactory.Report($"PRED|{url}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text.Length > 90 ? text[..90] : text)}");

        // Assert
        if (profile == "anonyme")
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        else if (profile is "admin" or "predreader")
        {
            // Autorisé : aucune base réelle, la société de test n'a ni salarié ni pointage.
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        else
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            // La base est contrôlée en premier (règle 4b-5) : le message de permission n'apparaît que pour un compte qui a accès à la base.
            if (profile is "gate6only" or "joel") text.Should().Contain(Denied);
            else text.Should().Contain("accès à cette base");
        }
    }

    [Fact]
    public async Task Landing_AuthorizedReader_ReturnsTheWeekWithoutAnyEmployee()
    {
        // Act
        var resp = await _http.SendAsync(_f.Request(
            "predreader", HttpMethod.Get, "/api/v1/predictions/landing?week=2026-11-11", sageHeader: ApiFactory.SageName));
        var text = await resp.Content.ReadAsStringAsync();

        // Assert : la semaine est ramenée à son lundi, les seuils viennent des règles HS.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("2026-11-09");
        text.Should().Contain("\"lignes\":[]");
    }

    [Fact]
    public async Task Landing_RefusedRead_WritesNothing()
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        await _http.SendAsync(_f.Request("gate6only", HttpMethod.Get, "/api/v1/predictions/landing", sageHeader: ApiFactory.SageName));

        // Assert
        _f.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("/api/v1/predictions/refresh")]
    [InlineData("/api/v1/predictions/train")]
    [InlineData("/api/v1/predictions/backfill")]
    public async Task FormerEngineTriggers_NoLongerExist(string url)
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Post, url));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
