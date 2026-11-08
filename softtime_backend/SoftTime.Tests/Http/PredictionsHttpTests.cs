using System.Net;

namespace SoftTime.Tests.Http;

/// <summary>
/// REPORTS:PREDICTIONS : les lectures des prévisions exigent la porte 6 ET la permission fine
/// (administrateur toujours accepté), avec le même message de refus que les lectures sensibles.
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

    // Lectures de prévisions (celles qui lisent des données par personne, plus l'état du moteur).
    public static IEnumerable<object[]> Reads()
    {
        foreach (var url in new[] { "/api/v1/predictions/status", "/api/v1/predictions/job", "/api/v1/predictions/overtime", "/api/v1/predictions/overtime/horizon", "/api/v1/predictions/overtime/accuracy" })
            foreach (var profile in new[] { "admin", "predreader", "prednobase", "gate6only", "user1", "param2", "joel", "anonyme" })
                yield return new object[] { url, profile };
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Prediction_read_needs_gate_6_and_the_fine_permission(string url, string profile)
    {
        // Arrange
        var sage = url.EndsWith("/job", StringComparison.Ordinal) ? null : ApiFactory.SageName;
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, url, sageHeader: sage));
        var text = await resp.Content.ReadAsStringAsync();

        // Act
        ApiFactory.Report($"PRED|{url}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text.Length > 90 ? text[..90] : text)}");

        // Assert
        if (profile == "anonyme")
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        else if (profile is "admin" or "predreader" || (profile == "prednobase" && url.EndsWith("/job", StringComparison.Ordinal)))
        {
            // Autorisé : la route répond (la base analytique est remplacée par une doublure vide dans ces tests).
            resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
            resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
            if (url.EndsWith("/job", StringComparison.Ordinal)) resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        else
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            // La base est contrôlée en premier (règle 4b-5) : le message de permission n'apparaît que pour un compte qui a accès à la base.
            if (profile is "gate6only" || profile == "joel" || url.EndsWith("/job", StringComparison.Ordinal)) text.Should().Contain(Denied);
            else text.Should().Contain("accès à cette base");
        }
    }

    [Fact]
    public async Task Gate_6_alone_without_the_prediction_permission_is_refused_even_on_the_job_state()
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("gate6only", HttpMethod.Get, "/api/v1/predictions/job"));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_refused_read_writes_nothing()
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        await _http.SendAsync(_f.Request("gate6only", HttpMethod.Get, "/api/v1/predictions/overtime", sageHeader: ApiFactory.SageName));

        // Assert
        _f.Snapshot().Should().Be(before);
    }

    // Étape 4b-11 : déclencher un traitement (entraînement, recalcul) est réservé aux administrateurs.
    public static IEnumerable<object[]> Triggers()
    {
        foreach (var url in new[] { "/api/v1/predictions/refresh", "/api/v1/predictions/train", "/api/v1/predictions/backfill" })
            foreach (var profile in new[] { "admin", "param2", "predreader", "gate6only", "joel", "anonyme" })
                yield return new object[] { url, profile };
    }

    [Theory]
    [MemberData(nameof(Triggers))]
    public async Task Training_actions_are_reserved_to_administrators(string url, string profile)
    {
        // Arrange
        var before = _f.Snapshot();
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Post, url));
        var text = await resp.Content.ReadAsStringAsync();

        // Act
        ApiFactory.Report($"4B11|{url}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text.Length > 90 ? text[..90] : text)}");

        // Assert
        if (profile == "anonyme")
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        else if (profile == "admin")
        {
            // Accepté, ou « déjà en cours » si un autre test a déjà programmé un traitement : jamais refusé.
            new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict }.Should().Contain(resp.StatusCode);
        }
        else
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            text.Should().Contain("Cette opération est réservée aux administrateurs.");
        }
        _f.Snapshot().Should().Be(before);
    }
}
