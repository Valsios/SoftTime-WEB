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
    private const string Url = "/api/v1/closing/analysis";
    private const string ChecklistUrl = "/api/v1/closing/checklist";
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
    private static void Seed(InMemoryUnitOfWork store)
    {
        var users = store.Get<Responsable>().Items;
        if (users.Any(u => u.IdResponsable == LecteurId))
            return;

        var perms = store.Get<UserPermission>().Items;
        users.Add(new Responsable { IdResponsable = LecteurId, IdRole = 91, Login = "closereader", NomResponsable = "closereader" });
        store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = (int)LecteurId, IdResponsable = LecteurId, IdBdd = 3033 });
        perms.Add(new UserPermission { IdResponsable = LecteurId, PermissionCode = "REPORTS:CLOSING", IsGranted = true });
        perms.Add(new UserPermission { IdResponsable = LecteurId, PermissionCode = $"SAGE:{ApiFactory.SageName}:READ", IsGranted = true });
        // L'analyse lit les pointages : la base pointeuse active doit aussi être lisible (comme le pré-traitement).
        perms.Add(new UserPermission { IdResponsable = LecteurId, PermissionCode = "POINTEUSE:PTG:READ", IsGranted = true });
        // Base SAGE cochée et lisible, mais sans lecture de la base pointeuse.
        users.Add(new Responsable { IdResponsable = SansPointeuseId, IdRole = 91, Login = "closenopte", NomResponsable = "closenopte" });
        store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = (int)SansPointeuseId, IdResponsable = SansPointeuseId, IdBdd = 3033 });
        perms.Add(new UserPermission { IdResponsable = SansPointeuseId, PermissionCode = "REPORTS:CLOSING", IsGranted = true });
        perms.Add(new UserPermission { IdResponsable = SansPointeuseId, PermissionCode = $"SAGE:{ApiFactory.SageName}:READ", IsGranted = true });
        // Permission fine de clôture, mais base non cochée dans « Accès bases ».
        users.Add(new Responsable { IdResponsable = SansBaseId, IdRole = 91, Login = "closenobase", NomResponsable = "closenobase" });
        perms.Add(new UserPermission { IdResponsable = SansBaseId, PermissionCode = "REPORTS:CLOSING", IsGranted = true });

        var lundi = new DateTime(2025, 11, 3);
        store.Get<CardPaie>().Items.Add(new CardPaie
        {
            Id = 70777, SageMatricule = "00777", SageNom = "Testnom", SagePrenom = "Testprenom",
            Departement = "DEP1", SageBdd = ApiFactory.SageName
        });
        store.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
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

    /// <summary>Champ <c>data</c> de l'enveloppe, sans l'horodatage qui change à chaque réponse.</summary>
    private static string Donnees(string texte)
    {
        using var json = JsonDocument.Parse(texte);
        return json.RootElement.GetProperty("data").GetRawText();
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
    public async Task Analysis_MissingDatabaseGateOrPermission_IsRefused(string profil, HttpStatusCode attendu)
    {
        // Arrange
        var before = _f.Snapshot();
        var (code, texte) = await Envoyer(_http, Requete(profil, HttpMethod.Get, $"{Url}?{Semaine}"));

        // Act
        ApiFactory.Report($"CLOSE|{profil}|{(int)code}|{(texte.Length > 90 ? texte[..90] : texte)}");

        // Assert
        code.Should().Be(attendu);
        if (profil is "closenobase" or "closenopte")
            texte.Should().Contain("accès à cette base");       // la base (SAGE puis pointeuse) est contrôlée en premier
        else if (attendu == HttpStatusCode.Forbidden)
            texte.Should().Contain(Denied);
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task Analysis_AuthorizedReader_ReturnsStatusScoreCountsAndAnomalies()
    {
        // Act
        var (code, texte) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&includeComparison=true"));

        // Assert
        code.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(texte);
        var r = json.RootElement.GetProperty("data");
        r.GetProperty("statut").GetString().Should().Be("Bloquant");
        r.GetProperty("statutLibelle").GetString().Should().Be("Bloquant");
        r.GetProperty("seuilBloquant").GetDecimal().Should().Be(0.05m);
        r.GetProperty("score").GetInt32().Should().Be(0);
        r.GetProperty("formule").GetString().Should().Contain("poids maximum");
        r.GetProperty("joursEmployeControles").GetInt32().Should().Be(1);
        r.GetProperty("nomsAffiches").GetBoolean().Should().BeTrue();
        r.GetProperty("comparaison").ValueKind.Should().Be(JsonValueKind.Null);
        r.GetProperty("parDepartement")[0].GetProperty("departement").GetString().Should().Be("DEP1");
        var anomalie = r.GetProperty("anomalies").GetProperty("items")[0];
        anomalie.GetProperty("type").GetString().Should().Be("PointageManquant");
        anomalie.GetProperty("matricule").GetString().Should().Be("777");
        anomalie.GetProperty("nom").GetString().Should().Be("Testnom");
        anomalie.GetProperty("prenom").GetString().Should().Be("Testprenom");
        anomalie.GetProperty("departement").GetString().Should().Be("DEP1");
    }

    [Fact]
    public async Task Analysis_IncludeInformation_ExposesExcludedEmployeeCount()
    {
        // Act
        var (code, texte) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&includeInformation=true"));

        // Assert
        code.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(texte);
        var r = json.RootElement.GetProperty("data");
        r.GetProperty("salariesSansPlanningExclus").GetInt32().Should().Be(0);
        r.GetProperty("parGravite").EnumerateArray().Should().Contain(g => g.GetProperty("cle").GetString() == "Information");
        r.GetProperty("anomalies").GetProperty("items")[0].TryGetProperty("pointageExiste", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Analysis_WithoutGravityParameters_IsIdenticalToEmptyOnes()
    {
        // Act
        var (c1, sans) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}"));
        var (c2, vides) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&gravites=&tri="));

        // Assert
        c1.Should().Be(HttpStatusCode.OK);
        c2.Should().Be(HttpStatusCode.OK);
        Donnees(vides).Should().Be(Donnees(sans));
    }

    [Fact]
    public async Task Analysis_GravityFilter_ChangesListOnlyNeverStatusScoreOrCounts()
    {
        // Arrange
        var (_, complet) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}"));

        // Act
        var (code, mineures) = await Envoyer(_http,
            Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&gravites=Mineure,Information&tri=gravite"));
        var (codeCritique, critiques) = await Envoyer(_http,
            Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&gravites=critique&tri=gravite"));

        // Assert
        code.Should().Be(HttpStatusCode.OK);
        codeCritique.Should().Be(HttpStatusCode.OK);
        using var a = JsonDocument.Parse(complet);
        using var b = JsonDocument.Parse(mineures);
        using var c = JsonDocument.Parse(critiques);
        var (ra, rb, rc) = (a.RootElement.GetProperty("data"), b.RootElement.GetProperty("data"), c.RootElement.GetProperty("data"));
        foreach (var champ in new[] { "statut", "score", "nombreAnomalies", "joursEmployeControles" })
        {
            rb.GetProperty(champ).ToString().Should().Be(ra.GetProperty(champ).ToString());
            rc.GetProperty(champ).ToString().Should().Be(ra.GetProperty(champ).ToString());
        }
        rb.GetProperty("parGravite").ToString().Should().Be(ra.GetProperty("parGravite").ToString());
        rb.GetProperty("anomalies").GetProperty("total").GetInt32().Should().Be(0);   // l'anomalie du jeu est Critique
        rc.GetProperty("anomalies").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData("gravites=Grave")]
    [InlineData("gravites=Critique,Urgent")]
    [InlineData("tri=date")]
    public async Task Analysis_InvalidGravityOrSortValue_IsRefusedWithClearMessage(string requete)
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var (code, texte) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}&{requete}"));

        // Assert
        code.Should().Be(HttpStatusCode.BadRequest);
        texte.Should().ContainEquivalentOf("gravite");
        _f.Snapshot().Should().Be(before);
    }

    // ---------- Checklist d'avancement ----------

    [Theory]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("closereader", HttpStatusCode.OK)]
    [InlineData("closenobase", HttpStatusCode.Forbidden)]
    [InlineData("closenopte", HttpStatusCode.Forbidden)]
    [InlineData("gate6only", HttpStatusCode.Forbidden)]
    [InlineData("joel", HttpStatusCode.Forbidden)]
    [InlineData("anonyme", HttpStatusCode.Unauthorized)]
    public async Task Checklist_AnyProfile_HasSameAccessRulesAsAnalysis(string profil, HttpStatusCode attendu)
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var (code, texte) = await Envoyer(_http, Requete(profil, HttpMethod.Get, $"{ChecklistUrl}?{Semaine}"));

        // Assert
        code.Should().Be(attendu);
        if (attendu == HttpStatusCode.Forbidden && profil is not ("closenobase" or "closenopte"))
            texte.Should().Contain(Denied);
        _f.Snapshot().Should().Be(before);                       // lecture seule
    }

    [Fact]
    public async Task Checklist_AuthorizedReader_ReturnsOneLinePerStepWithCountsOnly()
    {
        // Arrange
        var (_, analyseAvant) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}"));

        // Act
        var (code, texte) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{ChecklistUrl}?{Semaine}"));
        var (_, analyseApres) = await Envoyer(_http, Requete("closereader", HttpMethod.Get, $"{Url}?{Semaine}"));

        // Assert
        code.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(texte);
        var lignes = json.RootElement.GetProperty("data").GetProperty("lignes").EnumerateArray().ToList();
        // Pas de calcul EXO/IMPO : pas de ligne « heures imposables ».
        lignes.Select(l => l.GetProperty("code").GetString())
            .Should().Equal("Pointages", "Anomalies", "Semaines", "ExoImpo", "SynchroPaie");
        lignes.Select(l => l.GetProperty("etat").GetString())
            .Should().OnlyContain(e => e == "Fait" || e == "AFaire" || e == "Information" || e == "NonDeterminable");
        texte.Should().NotContain("777");                        // aucun matricule ni nom
        texte.Should().NotContain("Testnom");
        Donnees(analyseApres).Should().Be(Donnees(analyseAvant)); // l'analyse est inchangée
    }

    [Fact]
    public async Task Analysis_ShowNamesFalse_ReturnsMatriculeOnly()
    {
        // Arrange
        using var sut = AvecReglages(("Closing:ShowNames", "false"));

        // Act
        var (code, texte) = await Envoyer(sut.CreateClient(), Requete("admin", HttpMethod.Get, $"{Url}?{Semaine}"));

        // Assert
        code.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(texte);
        json.RootElement.GetProperty("data").GetProperty("nomsAffiches").GetBoolean().Should().BeFalse();
        var anomalie = json.RootElement.GetProperty("data").GetProperty("anomalies").GetProperty("items")[0];
        anomalie.GetProperty("matricule").GetString().Should().Be("777");
        anomalie.GetProperty("nom").ValueKind.Should().Be(JsonValueKind.Null);
        anomalie.GetProperty("prenom").ValueKind.Should().Be(JsonValueKind.Null);
        texte.Should().NotContain("Testnom");
        texte.Should().NotContain("Testprenom");
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
        // Act
        var (code, texte) = await Envoyer(_http, Requete("admin", HttpMethod.Get, $"{Url}?{query}"));

        // Assert
        code.Should().Be(HttpStatusCode.BadRequest);
        texte.Should().NotContain("Exception");
    }

    [Fact]
    public async Task Analysis_PageSizeAbove200_IsCappedTo200()
    {
        // Act
        var (code, texte) = await Envoyer(_http, Requete("admin", HttpMethod.Get, $"{Url}?{Semaine}&page=1&pageSize=5000"));

        // Assert
        code.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(texte);
        json.RootElement.GetProperty("data").GetProperty("anomalies").GetProperty("pageSize").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task Analysis_UtcDates_KeepTheRequestedDay()
    {
        // Act
        var (code, texte) = await Envoyer(_http, Requete("admin", HttpMethod.Get, $"{Url}?from=2025-11-03T00:00:00Z&to=2025-11-07T00:00:00Z"));

        // Assert
        code.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(texte);
        json.RootElement.GetProperty("data").GetProperty("from").GetString().Should().StartWith("2025-11-03");
        json.RootElement.GetProperty("data").GetProperty("to").GetString().Should().StartWith("2025-11-07");
    }

    // ---------- Lecture seule et désactivation ----------

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Analysis_WriteMethods_Return405AndWriteNothing(string methode)
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var (code, _) = await Envoyer(_http, Requete("admin", new HttpMethod(methode), $"{Url}?{Semaine}"));

        // Assert
        code.Should().Be(HttpStatusCode.MethodNotAllowed);
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task Analysis_FeatureDisabled_Returns404()
    {
        // Arrange
        using var sut = AvecReglages(("Closing:Enabled", "false"));

        // Act
        var (code, texte) = await Envoyer(sut.CreateClient(), Requete("admin", HttpMethod.Get, $"{Url}?{Semaine}"));

        // Assert
        code.Should().Be(HttpStatusCode.NotFound);
        texte.Should().Contain("désactivée");
    }

    [Fact]
    public async Task Analysis_FeatureDisabledWithoutPermission_IsRefused()
    {
        // Arrange
        using var sut = AvecReglages(("Closing:Enabled", "false"));

        // Act
        var (code, _) = await Envoyer(sut.CreateClient(), Requete("gate6only", HttpMethod.Get, $"{Url}?{Semaine}"));

        // Assert
        code.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void Startup_InvalidClosingSetting_FailsWithClearMessage()
    {
        // Arrange
        using var sut = AvecReglages(("Closing:CriticalRatioBlocking", "2"));

        // Act
        var act = () => sut.CreateClient();

        // Assert
        var erreur = act.Should().Throw<Exception>().Which;
        var validation = erreur as OptionsValidationException ?? erreur.InnerException as OptionsValidationException;
        validation.Should().NotBeNull();
        validation!.Message.Should().Contain("Closing:CriticalRatioBlocking doit être compris entre 0 et 1");
        validation.Message.Should().NotContain(ApiFactory.TestKey);
    }

    private WebApplicationFactory<Program> AvecReglages(params (string Cle, string Valeur)[] reglages)
        => _f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(reglages.Select(r => new KeyValuePair<string, string?>(r.Cle, r.Valeur)))));
}
