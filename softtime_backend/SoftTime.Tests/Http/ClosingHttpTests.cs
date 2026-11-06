using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// GET api/closing/analysis : lecture seule, base active puis porte 6 ET REPORTS:CLOSING
/// (administrateur toujours accepté), même message de refus que les prévisions. Les comptes de
/// clôture sont ajoutés au magasin en mémoire de la fabrique par cette classe (ApiFactory inchangée).
/// </summary>
public class ClosingHttpTests : IClassFixture<ApiFactory>
{
    private const string Denied = "Vous n'avez pas la permission requise pour cette consultation.";
    private const string Url = "/api/closing/analysis";
    private const string Semaine = "from=2025-11-03&to=2025-11-07";

    private const decimal LecteurId = 10094;
    private const decimal SansBaseId = 10095;
    private const decimal SansPointeuseId = 10096;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public ClosingHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (factory.Store)
            Seed(factory.Store);
    }

    /// <summary>
    /// Deux comptes (rôle 91 sans droits) et un salarié planifié le lundi 3 novembre 2025, sans pointage :
    /// une anomalie « pointage manquant » à présenter avec ses nom et prénom.
    /// </summary>
    private static void Seed(SnapshotInMemoryUnitOfWork store)
    {
        var users = store.GetRepo<Responsable>().Items;
        if (users.Any(u => u.IdResponsable == LecteurId))
            return;

        var perms = store.GetRepo<UserPermission>().Items;
        users.Add(new Responsable { IdResponsable = LecteurId, IdRole = 91, Login = "closereader", NomResponsable = "closereader" });
        store.GetRepo<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = (int)LecteurId, IdResponsable = LecteurId, IdBdd = 3033 });
        perms.Add(new UserPermission { IdResponsable = LecteurId, PermissionCode = "REPORTS:CLOSING", IsGranted = true });
        perms.Add(new UserPermission { IdResponsable = LecteurId, PermissionCode = $"SAGE:{ApiFactory.SageName}:READ", IsGranted = true });
        // L'analyse lit les pointages : la base pointeuse active doit aussi être lisible (comme le pré-traitement).
        perms.Add(new UserPermission { IdResponsable = LecteurId, PermissionCode = "POINTEUSE:PTG:READ", IsGranted = true });
        // Base SAGE cochée et lisible, mais sans lecture de la base pointeuse.
        users.Add(new Responsable { IdResponsable = SansPointeuseId, IdRole = 91, Login = "closenopte", NomResponsable = "closenopte" });
        store.GetRepo<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = (int)SansPointeuseId, IdResponsable = SansPointeuseId, IdBdd = 3033 });
        perms.Add(new UserPermission { IdResponsable = SansPointeuseId, PermissionCode = "REPORTS:CLOSING", IsGranted = true });
        perms.Add(new UserPermission { IdResponsable = SansPointeuseId, PermissionCode = $"SAGE:{ApiFactory.SageName}:READ", IsGranted = true });
        // Permission fine de clôture, mais base non cochée dans « Accès bases ».
        users.Add(new Responsable { IdResponsable = SansBaseId, IdRole = 91, Login = "closenobase", NomResponsable = "closenobase" });
        perms.Add(new UserPermission { IdResponsable = SansBaseId, PermissionCode = "REPORTS:CLOSING", IsGranted = true });

        var lundi = new DateTime(2025, 11, 3);
        store.GetRepo<CardPaie>().Items.Add(new CardPaie
        {
            Id = 70777, SageMatricule = "00777", SageNom = "Testnom", SagePrenom = "Testprenom",
            Departement = "DEP1", SageBdd = ApiFactory.SageName
        });
        store.GetRepo<PlanningSalarie>().Items.Add(new PlanningSalarie
        {
            Id = 70777, IdSal = 70777, DateP = lundi, Ha = lundi.AddHours(8), Hd = lundi.AddHours(17), OffP = false,
            BddSage = ApiFactory.SageName
        });
    }

    private static string? Jeton(string profil) => profil switch
    {
        "closereader" => ApiFactory.Token(LecteurId, 91, 1, 6),
        "closenobase" => ApiFactory.Token(SansBaseId, 91, 6),
        "closenopte" => ApiFactory.Token(SansPointeuseId, 91, 1, 6),
        _ => ApiFactory.TokenFor(profil)
    };

    private static HttpRequestMessage Requete(string profil, HttpMethod methode, string url, bool avecBase = true)
    {
        var req = new HttpRequestMessage(methode, url);
        if (Jeton(profil) is string t)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {t}");
        if (avecBase)
            req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
        return req;
    }

    private static async Task<(HttpStatusCode Code, string Texte)> Envoyer(HttpClient http, HttpRequestMessage req)
    {
        var resp = await http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    // ---------- Accès ----------

    [Theory]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("closereader", HttpStatusCode.OK)]
    [InlineData("closenobase", HttpStatusCode.Forbidden)]
    [InlineData("closenopte", HttpStatusCode.Forbidden)]
    [InlineData("gate6only", HttpStatusCode.Forbidden)]
    [InlineData("joel", HttpStatusCode.Forbidden)]
    [InlineData("anonyme", HttpStatusCode.Unauthorized)]
    public async Task Analysis_needs_the_database_then_gate_6_and_REPORTS_CLOSING(string profil, HttpStatusCode attendu)
    {
        var before = _f.Snapshot();
        var (code, texte) = await Envoyer(_http, Requete(profil, HttpMethod.Get, $"{Url}?{Semaine}"));
        ApiFactory.Report($"CLOSE|{profil}|{(int)code}|{(texte.Length > 90 ? texte[..90] : texte)}");

        Assert.Equal(attendu, code);
        if (profil is "closenobase" or "closenopte")
            Assert.Contains("accès à cette base", texte);       // la base (SAGE puis pointeuse) est contrôlée en premier
        else if (attendu == HttpStatusCode.Forbidden)
            Assert.Contains(Denied, texte);
        Assert.Equal(before, _f.Snapshot());                     // lecture seule
    }

    [Fact]
    public async Task Analysis_returns_status_score_formula_counts_and_named_anomalies()
    {
        var (code, texte) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&includeComparison=true"));

        Assert.Equal(HttpStatusCode.OK, code);
        using var json = JsonDocument.Parse(texte);
        var r = json.RootElement;
        Assert.Equal("Bloquant", r.GetProperty("statut").GetString());      // 1 critique sur 1 jour contrôlé
        Assert.Equal("Bloquant", r.GetProperty("statutLibelle").GetString());
        Assert.Equal(0.05m, r.GetProperty("seuilBloquant").GetDecimal());
        Assert.Equal(0, r.GetProperty("score").GetInt32());
        Assert.Contains("poids maximum", r.GetProperty("formule").GetString());
        Assert.Equal(1, r.GetProperty("joursEmployeControles").GetInt32());
        Assert.True(r.GetProperty("nomsAffiches").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("comparaison").ValueKind);   // période précédente sans donnée
        Assert.Equal("DEP1", r.GetProperty("parDepartement")[0].GetProperty("departement").GetString());

        var anomalie = r.GetProperty("anomalies").GetProperty("items")[0];
        Assert.Equal("PointageManquant", anomalie.GetProperty("type").GetString());
        Assert.Equal("777", anomalie.GetProperty("matricule").GetString());
        Assert.Equal("Testnom", anomalie.GetProperty("nom").GetString());
        Assert.Equal("Testprenom", anomalie.GetProperty("prenom").GetString());
        Assert.Equal("DEP1", anomalie.GetProperty("departement").GetString());
    }

    [Fact]
    public async Task Analysis_accepts_includeInformation_and_exposes_the_excluded_employee_count()
    {
        var (code, texte) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&includeInformation=true"));

        Assert.Equal(HttpStatusCode.OK, code);
        using var json = JsonDocument.Parse(texte);
        var r = json.RootElement;
        Assert.Equal(0, r.GetProperty("salariesSansPlanningExclus").GetInt32());
        Assert.Contains(r.GetProperty("parGravite").EnumerateArray(), g => g.GetProperty("cle").GetString() == "Information");
        Assert.True(r.GetProperty("anomalies").GetProperty("items")[0].TryGetProperty("pointageExiste", out _));
    }

    [Fact]
    public async Task ShowNames_false_returns_the_matricule_only_without_error()
    {
        using var factory = AvecReglages(("Closing:ShowNames", "false"));
        var (code, texte) = await Envoyer(factory.CreateClient(), Requete("admin", HttpMethod.Get, $"{Url}?{Semaine}"));

        Assert.Equal(HttpStatusCode.OK, code);
        using var json = JsonDocument.Parse(texte);
        Assert.False(json.RootElement.GetProperty("nomsAffiches").GetBoolean());
        var anomalie = json.RootElement.GetProperty("anomalies").GetProperty("items")[0];
        Assert.Equal("777", anomalie.GetProperty("matricule").GetString());
        Assert.Equal(JsonValueKind.Null, anomalie.GetProperty("nom").ValueKind);
        Assert.Equal(JsonValueKind.Null, anomalie.GetProperty("prenom").ValueKind);
        Assert.DoesNotContain("Testnom", texte);
        Assert.DoesNotContain("Testprenom", texte);
    }

    // ---------- Validation ----------

    [Theory]
    [InlineData("from=2025-11-07&to=2025-11-03")]          // fin avant début
    [InlineData("from=2024-01-01&to=2025-01-01")]          // 367 jours
    [InlineData("from=2025-11-03")]                        // une seule borne
    [InlineData("from=pas-une-date&to=2025-11-07")]        // date illisible
    [InlineData("from=2025-11-03&to=2025-11-07&page=0")]
    [InlineData("from=2025-11-03&to=2025-11-07&pageSize=0")]
    public async Task Invalid_period_or_paging_is_a_400(string query)
    {
        var (code, texte) = await Envoyer(_http, Requete("admin", HttpMethod.Get, $"{Url}?{query}"));

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.DoesNotContain("Exception", texte);
    }

    [Fact]
    public async Task PageSize_above_200_is_capped_to_200()
    {
        var (code, texte) = await Envoyer(_http, Requete("admin", HttpMethod.Get, $"{Url}?{Semaine}&page=1&pageSize=5000"));

        Assert.Equal(HttpStatusCode.OK, code);
        using var json = JsonDocument.Parse(texte);
        Assert.Equal(200, json.RootElement.GetProperty("anomalies").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Utc_dates_keep_the_requested_day()
    {
        var (code, texte) = await Envoyer(_http, Requete("admin", HttpMethod.Get, $"{Url}?from=2025-11-03T00:00:00Z&to=2025-11-07T00:00:00Z"));

        Assert.Equal(HttpStatusCode.OK, code);
        using var json = JsonDocument.Parse(texte);
        Assert.StartsWith("2025-11-03", json.RootElement.GetProperty("from").GetString());
        Assert.StartsWith("2025-11-07", json.RootElement.GetProperty("to").GetString());
    }

    // ---------- Lecture seule et désactivation ----------

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Write_methods_are_405_and_write_nothing(string methode)
    {
        var before = _f.Snapshot();
        var (code, _) = await Envoyer(_http, Requete("admin", new HttpMethod(methode), $"{Url}?{Semaine}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, code);
        Assert.Equal(before, _f.Snapshot());
    }

    [Fact]
    public async Task Disabled_feature_is_an_explicit_404_not_a_500()
    {
        using var factory = AvecReglages(("Closing:Enabled", "false"));
        var (code, texte) = await Envoyer(factory.CreateClient(), Requete("admin", HttpMethod.Get, $"{Url}?{Semaine}"));

        Assert.Equal(HttpStatusCode.NotFound, code);
        Assert.Contains("désactivée", texte);
    }

    [Fact]
    public async Task Disabled_feature_still_refuses_an_account_without_the_permission()
    {
        using var factory = AvecReglages(("Closing:Enabled", "false"));
        var (code, _) = await Envoyer(factory.CreateClient(), Requete("gate6only", HttpMethod.Get, $"{Url}?{Semaine}"));

        Assert.Equal(HttpStatusCode.Forbidden, code);
    }

    [Fact]
    public void Invalid_setting_refuses_startup_with_a_clear_message()
    {
        using var factory = AvecReglages(("Closing:CriticalRatioBlocking", "2"));

        var erreur = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var validation = erreur as OptionsValidationException ?? erreur.InnerException as OptionsValidationException;
        Assert.NotNull(validation);
        Assert.Contains("Closing:CriticalRatioBlocking doit être compris entre 0 et 1", validation!.Message);
        Assert.DoesNotContain(ApiFactory.TestKey, validation.Message);
    }

    private WebApplicationFactory<Program> AvecReglages(params (string Cle, string Valeur)[] reglages)
        => _f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(reglages.Select(r => new KeyValuePair<string, string?>(r.Cle, r.Valeur)))));
}
