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
        var sage = url.EndsWith("/job", StringComparison.Ordinal) ? null : ApiFactory.SageName;
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Get, url, sageHeader: sage));
        var text = await resp.Content.ReadAsStringAsync();
        ApiFactory.Report($"PRED|{url}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text.Length > 90 ? text[..90] : text)}");

        if (profile == "anonyme")
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        else if (profile is "admin" or "predreader" || (profile == "prednobase" && url.EndsWith("/job", StringComparison.Ordinal)))
        {
            // Autorisé : la route répond (la base analytique est remplacée par une doublure vide dans ces tests).
            Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
            if (url.EndsWith("/job", StringComparison.Ordinal)) Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            // La base est contrôlée en premier (règle 4b-5) : le message de permission n'apparaît que pour un compte qui a accès à la base.
            if (profile is "gate6only" || profile == "joel" || url.EndsWith("/job", StringComparison.Ordinal)) Assert.Contains(Denied, text);
            else Assert.Contains("accès à cette base", text);
        }
    }

    [Fact]
    public async Task Gate_6_alone_without_the_prediction_permission_is_refused_even_on_the_job_state()
    {
        var resp = await _http.SendAsync(_f.Request("gate6only", HttpMethod.Get, "/api/v1/predictions/job"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task A_refused_read_writes_nothing()
    {
        var before = _f.Snapshot();
        await _http.SendAsync(_f.Request("gate6only", HttpMethod.Get, "/api/v1/predictions/overtime", sageHeader: ApiFactory.SageName));
        Assert.Equal(before, _f.Snapshot());
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
        var before = _f.Snapshot();
        var resp = await _http.SendAsync(_f.Request(profile, HttpMethod.Post, url));
        var text = await resp.Content.ReadAsStringAsync();
        ApiFactory.Report($"4B11|{url}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text.Length > 90 ? text[..90] : text)}");

        if (profile == "anonyme")
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        else if (profile == "admin")
        {
            // Accepté, ou « déjà en cours » si un autre test a déjà programmé un traitement : jamais refusé.
            Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict });
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Contains("Cette opération est réservée aux administrateurs.", text);
        }
        Assert.Equal(before, _f.Snapshot());   // aucune donnée applicative n'est écrite
    }
}
