using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// « Mon espace » (api/me/space) : désactivé par défaut (404), puis garde dédiée (SELF:READ, matricule du jeton, une seule base
/// autorisée, matricule connu), absence de fuite entre salariés et réponses sans identifiant. Données fictives.
/// </summary>
public class SelfSpaceHttpTests : IClassFixture<ApiFactory>
{
    private const decimal AId = 10201;            // salarié A (matricule A100)
    private const decimal BId = 10202;            // salarié B (matricule B200)
    private const decimal SansMatriculeId = 10203;
    private const decimal MatriculeInconnuId = 10204;
    private const decimal DeuxBasesId = 10205;
    private const decimal SansPermissionId = 10206;
    private const decimal AutreBaseId = 10207;

    private const string MatA = "A100";
    // Un matricule = un seul compte (garde) : les comptes de refus ont chacun leur matricule, distinct de celui de A.
    private const string MatDeuxBases = "DB100";
    private const string MatSansPermission = "SP100";
    private const string MatAutreBase = "AB100";
    private const string MatB = "B200";
    private static readonly string[] Routes =
    {
        "/api/me/space/summary", "/api/me/space/weeks", "/api/me/space/anomalies", "/api/me/space/absences", "/api/me/space/delays"
    };

    private readonly ApiFactory _f;
    private readonly HttpClient _http;
    private readonly DateTime _aujourdhui = DateTime.Today;

    public SelfSpaceHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (factory.Store)
            Seed(factory.Store);
    }

    private string Periode => $"from={_aujourdhui.AddDays(-45):yyyy-MM-dd}&to={_aujourdhui:yyyy-MM-dd}";

    private static DateTime LundiDe(DateTime j) => j.Date.AddDays(-(((int)j.DayOfWeek + 6) % 7));

    private DateTime JourOuvre(int reculMini)
    {
        var j = _aujourdhui.AddDays(-reculMini);
        while (j.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            j = j.AddDays(-1);
        return j.Date;
    }

    private void Seed(SnapshotInMemoryUnitOfWork store)
    {
        var users = store.GetRepo<T_RESPONSABLE>().Items;
        if (users.Any(u => u.IDRESPONSABLE == AId))
            return;

        // Rôle « Salarié » : une seule permission, aucune porte (aucune ligne T_DROIT_ROLE).
        store.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 92, NOMROLE = "Salarié" });
        store.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { ID = 9201, IDROLE = 92, PERMISSION_CODE = "SELF:READ", IS_GRANTED = true });

        void Compte(decimal id, decimal role, string login, string? matricule, params int[] bases)
        {
            users.Add(new T_RESPONSABLE { IDRESPONSABLE = id, IDROLE = role, LOGIN = login, NOMRESPONSABLE = "salarie", MATRICULE = matricule });
            foreach (var b in bases)
                store.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = (int)id * 10 + b % 10, IDRESPONSABLE = id, IDBDD = b });
        }
        Compte(AId, 92, "self-a", MatA, 3033);
        Compte(BId, 92, "self-b", MatB, 3033);
        Compte(SansMatriculeId, 92, "self-sans-matricule", null, 3033);
        Compte(MatriculeInconnuId, 92, "self-inconnu", "ZZZ999", 3033);
        Compte(DeuxBasesId, 92, "self-deux-bases", MatDeuxBases, 3033, 3034);
        Compte(SansPermissionId, 91, "self-sans-permission", MatSansPermission, 3033);
        Compte(AutreBaseId, 92, "self-autre-base", MatAutreBase, 3034);

        var cartes = store.GetRepo<T_CARDPAIE>().Items;
        cartes.Add(new T_CARDPAIE { ID = 5001, SAGE_MATRICULE = MatA, SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG", DEPARTEMENT = "D1" });
        cartes.Add(new T_CARDPAIE { ID = 5002, SAGE_MATRICULE = MatB, SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG", DEPARTEMENT = "D1" });

        // Semaines : A a une semaine validée (10 h sup.) et une non validée (invisible) ; B a une semaine validée (25 h sup.).
        var hs = store.GetRepo<T_HS_VALIDER>().Items;
        hs.Add(new T_HS_VALIDER { ID = 1, BDD_SAGE = ApiFactory.SageName, MATRICULES = MatA, DATE_DEB = LundiDe(_aujourdhui.AddDays(-21)), DATE_FIN = LundiDe(_aujourdhui.AddDays(-21)).AddDays(6), HT = 40m, HS = 10m, VALIDATION_HS = true });
        hs.Add(new T_HS_VALIDER { ID = 2, BDD_SAGE = ApiFactory.SageName, MATRICULES = MatA, DATE_DEB = LundiDe(_aujourdhui.AddDays(-14)), DATE_FIN = LundiDe(_aujourdhui.AddDays(-14)).AddDays(6), HT = 44m, HS = 77m, VALIDATION_HS = false });
        hs.Add(new T_HS_VALIDER { ID = 3, BDD_SAGE = ApiFactory.SageName, MATRICULES = MatB, DATE_DEB = LundiDe(_aujourdhui.AddDays(-28)), DATE_FIN = LundiDe(_aujourdhui.AddDays(-28)).AddDays(6), HT = 50m, HS = 25m, VALIDATION_HS = true });

        var exo = store.GetRepo<T_HSExoImp>().Items;
        exo.Add(new T_HSExoImp { OID = 1, Matricule = MatA, PeriodeDebut = _aujourdhui.AddDays(-40), PeriodeFin = _aujourdhui.AddDays(-10), EXO130 = 8m, EXO150 = 2m, I130 = 0m, I150 = 0m, BDD_SAGE = ApiFactory.SageName });
        exo.Add(new T_HSExoImp { OID = 2, Matricule = MatB, PeriodeDebut = _aujourdhui.AddDays(-40), PeriodeFin = _aujourdhui.AddDays(-10), EXO130 = 20m, EXO150 = 0m, I130 = 5m, I150 = 0m, BDD_SAGE = ApiFactory.SageName });

        var anomalies = store.GetRepo<T_HEUREANOMALIE>().Items;
        anomalies.Add(new T_HEUREANOMALIE { ID = 1, MATRICULE_SAGE = MatA, DATE_POINTAGE_IN = JourOuvre(5), H1 = TimeSpan.FromHours(8), BDD_SAGE = ApiFactory.SageName });
        anomalies.Add(new T_HEUREANOMALIE { ID = 2, MATRICULE_SAGE = MatB, DATE_POINTAGE_IN = JourOuvre(6), H1 = TimeSpan.FromHours(9), BDD_SAGE = ApiFactory.SageName });
        anomalies.Add(new T_HEUREANOMALIE { ID = 3, MATRICULE_SAGE = MatB, DATE_POINTAGE_IN = JourOuvre(8), ABS_AM = true, ABS_PM = true, BDD_SAGE = ApiFactory.SageName });

        // Plannings 8 h - 17 h : A est en retard de 20 min un jour et absent sans justification un autre jour ; B en retard de 45 min.
        var plannings = store.GetRepo<T_PLANNING_SAL>().Items;
        void Plan(int id, int idsal, DateTime jour) => plannings.Add(new T_PLANNING_SAL
        {
            ID = id, IDSAL = idsal, DateP = jour, HA = jour.Add(TimeSpan.FromHours(8)), HD = jour.Add(TimeSpan.FromHours(17)),
            OffP = false, BDD_SAGE = ApiFactory.SageName, BDD_POINTEUSE = "PTG"
        });
        var jourRetardA = JourOuvre(10);
        var jourAbsentA = JourOuvre(12);
        var jourRetardB = JourOuvre(10);
        Plan(1, 5001, jourRetardA);
        Plan(2, 5001, jourAbsentA);
        Plan(3, 5002, jourRetardB);
        var corrections = store.GetRepo<T_HEURECORRIGER>().Items;
        corrections.Add(new T_HEURECORRIGER { ID = 1, MATRICULE_SAGE = MatA, DATE_POINTAGE_IN = jourRetardA, HENTREE = new TimeSpan(8, 20, 0), HSORTIE = new TimeSpan(17, 0, 0), BDD_SAGE = ApiFactory.SageName });
        corrections.Add(new T_HEURECORRIGER { ID = 2, MATRICULE_SAGE = MatB, DATE_POINTAGE_IN = jourRetardB, HENTREE = new TimeSpan(8, 45, 0), HSORTIE = new TimeSpan(17, 0, 0), BDD_SAGE = ApiFactory.SageName });
    }

    // ---------- outils ----------

    private static string Jeton(decimal userId, decimal roleId, string matricule)
    {
        var uid = ((long)userId).ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, uid), new(ClaimTypes.NameIdentifier, uid), new("uid", uid),
            new(ClaimTypes.Name, $"test{uid}"), new("roleId", ((long)roleId).ToString()),
            new("matricule", matricule), new("rights", string.Empty)
        };
        var token = new JwtSecurityToken(ApiFactory.TestIssuer, ApiFactory.TestAudience, claims, expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.TestKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private WebApplicationFactory<Program> Actif()
        => _f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("SelfService:Enabled", "true") })));

    private static async Task<(HttpStatusCode Code, string Texte)> Appeler(HttpClient http, HttpMethod methode, string url, string? jeton,
        string? base_ = null, params (string Nom, string Valeur)[] entetes)
    {
        var req = new HttpRequestMessage(methode, url);
        if (jeton is not null)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {jeton}");
        if (base_ is not null)
            req.Headers.TryAddWithoutValidation("X-Sage-Database", base_);
        foreach (var (nom, valeur) in entetes)
            req.Headers.TryAddWithoutValidation(nom, valeur);
        var resp = await http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private string Url(string route, string? supplement = null) => $"{route}?{Periode}{(supplement is null ? string.Empty : "&" + supplement)}";

    // ---------- désactivée par défaut ----------

    [Fact]
    public async Task Disabled_by_default_every_route_answers_404()
    {
        var avant = _f.Snapshot();
        foreach (var route in Routes)
        {
            var (code, texte) = await Appeler(_http, HttpMethod.Get, Url(route), Jeton(AId, 92, MatA));
            Assert.True(code == HttpStatusCode.NotFound, $"{route} : attendu 404, obtenu {(int)code}");
            Assert.Contains("pas activé", texte);
        }
        Assert.Equal(avant, _f.Snapshot());
    }

    // ---------- accès de base ----------

    [Fact]
    public async Task Anonymous_gets_401()
    {
        using var f = Actif();
        foreach (var route in Routes)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Appeler(f.CreateClient(), HttpMethod.Get, Url(route), null)).Code);
    }

    [Fact]
    public async Task Only_GET_exists_other_methods_are_refused_and_nothing_is_written()
    {
        using var f = Actif();
        var avant = _f.Snapshot();
        foreach (var route in Routes)
            foreach (var methode in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
            {
                var (code, _) = await Appeler(f.CreateClient(), methode, Url(route), Jeton(AId, 92, MatA));
                Assert.True(code == HttpStatusCode.MethodNotAllowed, $"{methode} {route} : attendu 405, obtenu {(int)code}");
            }
        Assert.Equal(avant, _f.Snapshot());
    }

    [Fact]
    public async Task A_salarie_reads_only_his_own_data_on_every_route()
    {
        using var f = Actif();
        var http = f.CreateClient();
        var avant = _f.Snapshot();

        var (cs, summary) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/summary"), Jeton(AId, 92, MatA));
        var (cw, weeks) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/weeks"), Jeton(AId, 92, MatA));
        var (ca, anomalies) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/anomalies"), Jeton(AId, 92, MatA));
        var (cb, absences) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/absences"), Jeton(AId, 92, MatA));
        var (cd, delays) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/delays"), Jeton(AId, 92, MatA));

        Assert.All(new[] { cs, cw, ca, cb, cd }, c => Assert.Equal(HttpStatusCode.OK, c));
        using (var j = JsonDocument.Parse(weeks))
        {
            var semaines = j.RootElement.GetProperty("semaines");
            Assert.Equal(1, semaines.GetArrayLength());                                   // la semaine non validée est invisible
            Assert.Equal(10m, semaines[0].GetProperty("heuresSupplementaires").GetDecimal());
            Assert.Equal(8m, semaines[0].GetProperty("hs130").GetDecimal());
            Assert.Equal(2m, semaines[0].GetProperty("hs150").GetDecimal());
            Assert.Equal(1, j.RootElement.GetProperty("exoImpo").GetArrayLength());
            Assert.Equal(8m, j.RootElement.GetProperty("exoImpo")[0].GetProperty("exo130").GetDecimal());
        }
        using (var j = JsonDocument.Parse(anomalies))
            Assert.Equal(1, j.RootElement.GetProperty("anomalies").GetArrayLength());
        using (var j = JsonDocument.Parse(delays))
        {
            var retards = j.RootElement.GetProperty("retards");
            Assert.Equal(1, retards.GetArrayLength());
            Assert.Equal(20, retards[0].GetProperty("minutesRetard").GetInt32());
        }
        using (var j = JsonDocument.Parse(absences))
            Assert.Contains(j.RootElement.GetProperty("absences").EnumerateArray(), a => a.GetProperty("type").GetString() == "Non justifiée");
        using (var j = JsonDocument.Parse(summary))
        {
            Assert.Equal(1, j.RootElement.GetProperty("semainesValidees").GetInt32());
            Assert.Equal(1, j.RootElement.GetProperty("anomalies").GetInt32());
            Assert.Equal(1, j.RootElement.GetProperty("retards").GetInt32());
        }
        Assert.Equal(avant, _f.Snapshot());                                               // lecture seule
    }

    [Fact]
    public async Task Responses_carry_no_identifier_no_matricule_and_nothing_of_another_salarie()
    {
        using var f = Actif();
        var http = f.CreateClient();
        foreach (var route in Routes)
        {
            var (code, texte) = await Appeler(http, HttpMethod.Get, Url(route), Jeton(AId, 92, MatA));
            Assert.Equal(HttpStatusCode.OK, code);
            Assert.DoesNotContain(MatA, texte);
            Assert.DoesNotContain(MatB, texte);
            using var json = JsonDocument.Parse(texte);
            AssertSansIdentifiant(json.RootElement, route);
        }
    }

    private static void AssertSansIdentifiant(JsonElement element, string route)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject())
            {
                var nom = p.Name.ToLowerInvariant();
                Assert.False(nom is "id" or "oid" or "matricule" or "matricules" or "idsal" or "nom" or "prenom" or "login",
                    $"{route} : champ interdit « {p.Name} »");
                AssertSansIdentifiant(p.Value, route);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var e in element.EnumerateArray())
                AssertSansIdentifiant(e, route);
    }

    // ---------- fuite entre salariés ----------

    [Fact]
    public async Task A_never_reads_B_even_with_a_matricule_parameter_a_header_or_a_route_value()
    {
        using var f = Actif();
        var http = f.CreateClient();
        foreach (var route in Routes)
        {
            var (_, normal) = await Appeler(http, HttpMethod.Get, Url(route), Jeton(AId, 92, MatA));
            foreach (var supplement in new[] { $"matricule={MatB}", $"Matricule={MatB}", $"matriculeFrom={MatB}&matriculeTo={MatB}", $"cle={MatB}", $"userId={BId}" })
            {
                var (code, texte) = await Appeler(http, HttpMethod.Get, Url(route, supplement), Jeton(AId, 92, MatA), null,
                    ("X-Matricule", MatB), ("X-User-Id", BId.ToString()));
                Assert.Equal(HttpStatusCode.OK, code);
                Assert.Equal(normal, texte);                                              // la réponse de A reste exactement la même
            }
        }
    }

    [Fact]
    public async Task B_reads_his_own_data_which_differ_from_A()
    {
        using var f = Actif();
        var http = f.CreateClient();

        var (_, weeks) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/weeks"), Jeton(BId, 92, MatB));
        var (_, delays) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/delays"), Jeton(BId, 92, MatB));
        var (_, anomalies) = await Appeler(http, HttpMethod.Get, Url("/api/me/space/anomalies"), Jeton(BId, 92, MatB));

        using (var j = JsonDocument.Parse(weeks))
        {
            Assert.Equal(25m, j.RootElement.GetProperty("semaines")[0].GetProperty("heuresSupplementaires").GetDecimal());
            Assert.Equal(5m, j.RootElement.GetProperty("exoImpo")[0].GetProperty("imp130").GetDecimal());
        }
        using (var j = JsonDocument.Parse(delays))
            Assert.Equal(45, j.RootElement.GetProperty("retards")[0].GetProperty("minutesRetard").GetInt32());
        using (var j = JsonDocument.Parse(anomalies))
            Assert.Equal(2, j.RootElement.GetProperty("anomalies").GetArrayLength());
    }

    [Fact]
    public async Task A_token_matricule_that_differs_from_the_account_row_is_refused()
    {
        // Correction (revue) : le claim du jeton n est plus la seule source. Le compte A est relu à chaque appel ; un jeton qui
        // prétend porter le matricule de B alors que la base dit A est refusé (403), sans aucune donnée de A ni de B.
        using var f = Actif();
        var (code, texte) = await Appeler(f.CreateClient(), HttpMethod.Get, Url("/api/me/space/weeks"), Jeton(AId, 92, MatB));

        Assert.Equal(HttpStatusCode.Forbidden, code);
        Assert.Contains("a changé", texte);
        Assert.DoesNotContain("semaines", texte);
    }

    // ---------- refus explicites ----------

    [Theory]
    [InlineData("sans-matricule", "Aucun matricule")]
    [InlineData("matricule-inconnu", "inconnu")]
    [InlineData("deux-bases", "exactement une base")]
    [InlineData("sans-permission", "pas accès")]
    [InlineData("autre-base", "inconnu")]
    [InlineData("en-tete-autre-base", "accès à cette base")]
    [InlineData("administrateur", "pas accès")]
    public async Task Refusals_are_403_with_an_explicit_message_on_every_route(string cas, string messageAttendu)
    {
        using var f = Actif();
        var http = f.CreateClient();
        var avant = _f.Snapshot();
        foreach (var route in Routes)
        {
            var (jeton, base_) = cas switch
            {
                "sans-matricule" => (Jeton(SansMatriculeId, 92, string.Empty), null),
                "matricule-inconnu" => (Jeton(MatriculeInconnuId, 92, "ZZZ999"), null),
                "deux-bases" => (Jeton(DeuxBasesId, 92, MatDeuxBases), null),
                "sans-permission" => (Jeton(SansPermissionId, 91, MatSansPermission), null),
                "autre-base" => (Jeton(AutreBaseId, 92, MatAutreBase), null),
                "en-tete-autre-base" => (Jeton(AId, 92, MatA), ApiFactory.OtherSageName),
                _ => (ApiFactory.TokenFor("admin")!, (string?)null)
            };
            var (code, texte) = await Appeler(http, HttpMethod.Get, Url(route), jeton, base_);
            Assert.True(code == HttpStatusCode.Forbidden, $"{cas} {route} : attendu 403, obtenu {(int)code}");
            Assert.Contains(messageAttendu, texte);
        }
        Assert.Equal(avant, _f.Snapshot());
    }

    [Fact]
    public async Task A_database_header_sent_by_a_salarie_is_refused_upstream_so_the_client_must_not_send_one()
    {
        // TenantMiddleware exige la porte 1 dès qu'un en-tête X-Sage-Database est présent : un salarié (sans porte) n'en envoie jamais,
        // et la base vient alors de sa seule base autorisée. L'en-tête ne peut donc ni choisir ni changer la base.
        using var f = Actif();
        var (avecEntete, _) = await Appeler(f.CreateClient(), HttpMethod.Get, Url("/api/me/space/summary"), Jeton(AId, 92, MatA), ApiFactory.SageName);
        var (sansEntete, _) = await Appeler(f.CreateClient(), HttpMethod.Get, Url("/api/me/space/summary"), Jeton(AId, 92, MatA));

        Assert.Equal(HttpStatusCode.Forbidden, avecEntete);
        Assert.Equal(HttpStatusCode.OK, sansEntete);
    }

    // ---------- période ----------

    [Fact]
    public async Task Period_is_bounded_to_four_months_inside_the_last_four_months_and_without_future_days()
    {
        using var f = Actif();
        var http = f.CreateClient();
        var jeton = Jeton(AId, 92, MatA);
        string Q(DateTime a, DateTime b) => $"/api/me/space/weeks?from={a:yyyy-MM-dd}&to={b:yyyy-MM-dd}";
        var debutMois = new DateTime(_aujourdhui.Year, _aujourdhui.Month, 1);

        Assert.Equal(HttpStatusCode.BadRequest, (await Appeler(http, HttpMethod.Get, Q(debutMois.AddMonths(-4), _aujourdhui), jeton)).Code);   // hors fenêtre
        Assert.Equal(HttpStatusCode.BadRequest, (await Appeler(http, HttpMethod.Get, Q(_aujourdhui, _aujourdhui.AddDays(-1)), jeton)).Code);     // fin avant début
        Assert.Equal(HttpStatusCode.BadRequest, (await Appeler(http, HttpMethod.Get, Q(_aujourdhui.AddDays(1), _aujourdhui.AddDays(5)), jeton)).Code); // futur
        Assert.Equal(HttpStatusCode.OK, (await Appeler(http, HttpMethod.Get, Q(debutMois.AddMonths(-3), _aujourdhui), jeton)).Code);             // 4 mois
        Assert.Equal(HttpStatusCode.OK, (await Appeler(http, HttpMethod.Get, "/api/me/space/weeks", jeton)).Code);                               // défaut : mois en cours
        Assert.Equal(HttpStatusCode.OK, (await Appeler(http, HttpMethod.Get, Q(_aujourdhui.AddDays(-3), _aujourdhui.AddDays(30)), jeton)).Code); // fin plafonnée à aujourd'hui
    }

    // ---------- un compte Salarié n'accède à aucun écran de gestion ----------

    [Theory]
    [InlineData("GET", "/api/cardpaie")]
    [InlineData("GET", "/api/dashboard/filters")]
    [InlineData("GET", "/api/users")]
    [InlineData("GET", "/api/roles")]
    [InlineData("GET", "/api/closing/analysis")]
    [InlineData("GET", "/api/reports/pointage")]
    [InlineData("GET", "/api/anomalies")]
    [InlineData("GET", "/api/overtime/exo")]
    [InlineData("GET", "/api/journal")]
    [InlineData("POST", "/api/sage-databases/activate")]
    public async Task A_salarie_account_is_refused_on_management_routes(string methode, string url)
    {
        using var f = Actif();
        var (code, _) = await Appeler(f.CreateClient(), new HttpMethod(methode), url, Jeton(AId, 92, MatA), ApiFactory.SageName);

        Assert.True(code == HttpStatusCode.Forbidden, $"{methode} {url} : attendu 403, obtenu {(int)code}");
    }

    // ---------- le rôle Salarié n'obtient jamais la porte 1 ----------

    [Fact]
    public async Task A_salarie_with_a_ticked_database_and_SELF_READ_does_not_get_gate_1_or_any_database_permission()
    {
        using var f = Actif();
        using var scope = f.Services.CreateScope();
        var droits = await scope.ServiceProvider.GetRequiredService<EffectiveRightsService>().GetAsync(AId, fresh: true);

        Assert.True(droits.Has("SELF:READ"));
        Assert.Empty(droits.Gates);                                                       // aucune porte, donc pas la porte 1
        Assert.False(droits.HasGate(1));
        Assert.DoesNotContain(droits.Permissions, p => p.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase)
                                                        || p.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase));
        Assert.False(droits.CanUseDatabase(SoftTime.Application.Services.DatabaseKind.Sage, ApiFactory.SageName));   // la base cochée ne suffit pas à utiliser les services de données
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;

    [Fact]
    public void The_Salarie_role_script_never_grants_a_database_permission_nor_a_gate()
    {
        var source = SourcePath();
        // .../softtime_backend/SoftTime.Tests/Http/<ce fichier> -> .../softtime_backend/migrations
        var dossier = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "migrations"));
        var script = Path.Combine(dossier, "mon_espace_role_salarie.sql");
        Assert.True(File.Exists(script), $"script introuvable : {script}");
        // Seules les instructions comptent : les commentaires du script expliquent justement ce qu'il ne doit pas faire.
        var sql = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(script), @"/\*.*?\*/|--[^
]*", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.Contains("SELF:READ", sql);
        Assert.DoesNotContain("SAGE:", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("POINTEUSE:", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PARAMS:", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("T_DROIT_ROLE", sql, StringComparison.OrdinalIgnoreCase);    // aucune porte pour ce rôle
        Assert.DoesNotContain("T_BDD_AUTORISE", sql, StringComparison.OrdinalIgnoreCase);  // aucun accès base créé par le script
    }
}
