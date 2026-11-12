using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// « Mon espace » (api/v1/me/space) : désactivé par défaut (404), puis garde dédiée (self.read, matricule du jeton, une seule base
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
        "/api/v1/me/space/summary", "/api/v1/me/space/weeks", "/api/v1/me/space/anomalies", "/api/v1/me/space/absences", "/api/v1/me/space/delays"
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

    private void Seed(InMemoryUnitOfWork store)
    {
        var users = store.Get<Responsable>().Items;
        if (users.Any(u => u.IdResponsable == AId))
            return;

        // Rôle « Salarié » : une seule permission, aucune porte (aucune ligne DroitRole).
        store.Get<Role>().Items.Add(new Role { IdRole = 92, NomRole = "Salarié" });
        store.Get<RolePermission>().Items.Add(new RolePermission { Id = 9201, IdRole = 92, PermissionCode = "self.read", IsGranted = true });

        void Compte(decimal id, decimal role, string login, string? matricule, params int[] bases)
        {
            users.Add(new Responsable { IdResponsable = id, IdRole = role, Login = login, NomResponsable = "salarie", Matricule = matricule });
            foreach (var b in bases)
                store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = (int)id * 10 + b % 10, IdResponsable = id, IdBdd = b });
        }
        Compte(AId, 92, "self-a", MatA, 3033);
        Compte(BId, 92, "self-b", MatB, 3033);
        Compte(SansMatriculeId, 92, "self-sans-matricule", null, 3033);
        Compte(MatriculeInconnuId, 92, "self-inconnu", "ZZZ999", 3033);
        Compte(DeuxBasesId, 92, "self-deux-bases", MatDeuxBases, 3033, 3034);
        Compte(SansPermissionId, 91, "self-sans-permission", MatSansPermission, 3033);
        Compte(AutreBaseId, 92, "self-autre-base", MatAutreBase, 3034);

        var cartes = store.Get<CardPaie>().Items;
        cartes.Add(new CardPaie { Id = 5001, SageMatricule = MatA, SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG", Departement = "D1" });
        cartes.Add(new CardPaie { Id = 5002, SageMatricule = MatB, SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG", Departement = "D1" });

        // Semaines : A a une semaine validée (10 h sup.) et une non validée (invisible) ; B a une semaine validée (25 h sup.).
        var hs = store.Get<HsValidee>().Items;
        hs.Add(new HsValidee { Id = 1, BddSage = ApiFactory.SageName, Matricules = MatA, DateDeb = LundiDe(_aujourdhui.AddDays(-21)), DateFin = LundiDe(_aujourdhui.AddDays(-21)).AddDays(6), Ht = 40m, Hs = 10m, ValidationHs = true });
        hs.Add(new HsValidee { Id = 2, BddSage = ApiFactory.SageName, Matricules = MatA, DateDeb = LundiDe(_aujourdhui.AddDays(-14)), DateFin = LundiDe(_aujourdhui.AddDays(-14)).AddDays(6), Ht = 44m, Hs = 77m, ValidationHs = false });
        hs.Add(new HsValidee { Id = 3, BddSage = ApiFactory.SageName, Matricules = MatB, DateDeb = LundiDe(_aujourdhui.AddDays(-28)), DateFin = LundiDe(_aujourdhui.AddDays(-28)).AddDays(6), Ht = 50m, Hs = 25m, ValidationHs = true });

        var exo = store.Get<HsExoImp>().Items;
        exo.Add(new HsExoImp { Oid = 1, Matricule = MatA, PeriodeDebut = _aujourdhui.AddDays(-40), PeriodeFin = _aujourdhui.AddDays(-10), Exo130 = 8m, Exo150 = 2m, I130 = 0m, I150 = 0m, BddSage = ApiFactory.SageName });
        exo.Add(new HsExoImp { Oid = 2, Matricule = MatB, PeriodeDebut = _aujourdhui.AddDays(-40), PeriodeFin = _aujourdhui.AddDays(-10), Exo130 = 20m, Exo150 = 0m, I130 = 5m, I150 = 0m, BddSage = ApiFactory.SageName });

        var anomalies = store.Get<HeureAnomalie>().Items;
        anomalies.Add(new HeureAnomalie { Id = 1, MatriculeSage = MatA, DatePointageIn = JourOuvre(5), H1 = TimeSpan.FromHours(8), BddSage = ApiFactory.SageName });
        anomalies.Add(new HeureAnomalie { Id = 2, MatriculeSage = MatB, DatePointageIn = JourOuvre(6), H1 = TimeSpan.FromHours(9), BddSage = ApiFactory.SageName });
        anomalies.Add(new HeureAnomalie { Id = 3, MatriculeSage = MatB, DatePointageIn = JourOuvre(8), AbsAm = true, AbsPm = true, BddSage = ApiFactory.SageName });

        // Plannings 8 h - 17 h : A est en retard de 20 min un jour et absent sans justification un autre jour ; B en retard de 45 min.
        var plannings = store.Get<PlanningSalarie>().Items;
        void Plan(int id, int idsal, DateTime jour) => plannings.Add(new PlanningSalarie
        {
            Id = id, IdSal = idsal, DateP = jour, Ha = jour.Add(TimeSpan.FromHours(8)), Hd = jour.Add(TimeSpan.FromHours(17)),
            OffP = false, BddSage = ApiFactory.SageName, BddPointeuse = "PTG"
        });
        var jourRetardA = JourOuvre(10);
        var jourAbsentA = JourOuvre(12);
        var jourRetardB = JourOuvre(10);
        Plan(1, 5001, jourRetardA);
        Plan(2, 5001, jourAbsentA);
        Plan(3, 5002, jourRetardB);
        var corrections = store.Get<HeureCorrigee>().Items;
        corrections.Add(new HeureCorrigee { Id = 1, MatriculeSage = MatA, DatePointageIn = jourRetardA, HEntree = new TimeSpan(8, 20, 0), HSortie = new TimeSpan(17, 0, 0), BddSage = ApiFactory.SageName });
        corrections.Add(new HeureCorrigee { Id = 2, MatriculeSage = MatB, DatePointageIn = jourRetardB, HEntree = new TimeSpan(8, 45, 0), HSortie = new TimeSpan(17, 0, 0), BddSage = ApiFactory.SageName });
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
        string? baseSage = null, params (string Nom, string Valeur)[] entetes)
    {
        var req = new HttpRequestMessage(methode, url);
        if (jeton is not null)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {jeton}");
        if (baseSage is not null)
            req.Headers.TryAddWithoutValidation("X-Sage-Database", baseSage);
        foreach (var (nom, valeur) in entetes)
            req.Headers.TryAddWithoutValidation(nom, valeur);
        var resp = await http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private string Url(string route, string? supplement = null) => $"{route}?{Periode}{(supplement is null ? string.Empty : "&" + supplement)}";

    // ---------- désactivée par défaut ----------

    [Fact]
    public async Task SelfSpace_DisabledByDefault_EveryRouteAnswers404()
    {
        // Arrange
        var avant = _f.Snapshot();

        foreach (var route in Routes)
        {
            // Act
            var (code, texte) = await Appeler(_http, HttpMethod.Get, Url(route), Jeton(AId, 92, MatA));

            // Assert
            code.Should().Be(HttpStatusCode.NotFound, $"{route} : attendu 404, obtenu {(int)code}");
            texte.Should().Contain("pas activé");
        }
        _f.Snapshot().Should().Be(avant);
    }

    // ---------- accès de base ----------

    [Fact]
    public async Task SelfSpace_Anonymous_Gets401()
    {
        // Arrange
        using var f = Actif();

        foreach (var route in Routes)
        {
            // Act
            var (code, _) = await Appeler(f.CreateClient(), HttpMethod.Get, Url(route), null);

            // Assert
            code.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task SelfSpace_MethodOtherThanGet_Is405AndWritesNothing()
    {
        // Arrange
        using var f = Actif();
        var avant = _f.Snapshot();

        foreach (var route in Routes)
            foreach (var methode in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
            {
                // Act
                var (code, _) = await Appeler(f.CreateClient(), methode, Url(route), Jeton(AId, 92, MatA));

                // Assert
                code.Should().Be(HttpStatusCode.MethodNotAllowed, $"{methode} {route} : attendu 405, obtenu {(int)code}");
            }
        _f.Snapshot().Should().Be(avant);
    }

    [Fact]
    public async Task SelfSpace_Salarie_ReadsOnlyOwnDataOnEveryRoute()
    {
        // Arrange
        using var f = Actif();
        var http = f.CreateClient();
        var avant = _f.Snapshot();

        // Act
        var (cs, summary) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/summary"), Jeton(AId, 92, MatA));
        var (cw, weeks) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/weeks"), Jeton(AId, 92, MatA));
        var (ca, anomalies) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/anomalies"), Jeton(AId, 92, MatA));
        var (cb, absences) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/absences"), Jeton(AId, 92, MatA));
        var (cd, delays) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/delays"), Jeton(AId, 92, MatA));

        // Assert
        new[] { cs, cw, ca, cb, cd }.Should().OnlyContain(c => c == HttpStatusCode.OK);
        using (var j = JsonDocument.Parse(weeks))
        {
            var donnees = j.RootElement.GetProperty("data");
            var semaines = donnees.GetProperty("semaines");
            semaines.GetArrayLength().Should().Be(1);                                   // la semaine non validée est invisible
            semaines[0].GetProperty("heuresSupplementaires").GetDecimal().Should().Be(10m);
            semaines[0].GetProperty("hs130").GetDecimal().Should().Be(8m);
            semaines[0].GetProperty("hs150").GetDecimal().Should().Be(2m);
            donnees.GetProperty("exoImpo").GetArrayLength().Should().Be(1);
            donnees.GetProperty("exoImpo")[0].GetProperty("exo130").GetDecimal().Should().Be(8m);
        }
        Donnee(anomalies, "anomalies").GetArrayLength().Should().Be(1);
        var retards = Donnee(delays, "retards");
        retards.GetArrayLength().Should().Be(1);
        retards[0].GetProperty("minutesRetard").GetInt32().Should().Be(20);
        Donnee(absences, "absences").EnumerateArray().Should().Contain(a => a.GetProperty("type").GetString() == "Non justifiée");
        Donnee(summary, "semainesValidees").GetInt32().Should().Be(1);
        Donnee(summary, "anomalies").GetInt32().Should().Be(1);
        Donnee(summary, "retards").GetInt32().Should().Be(1);
        _f.Snapshot().Should().Be(avant);                                               // lecture seule
    }

    [Fact]
    public async Task SelfSpace_AnyRoute_ResponseHasNoIdentifierNorOtherSalarieData()
    {
        // Arrange
        using var f = Actif();
        var http = f.CreateClient();

        foreach (var route in Routes)
        {
            // Act
            var (code, texte) = await Appeler(http, HttpMethod.Get, Url(route), Jeton(AId, 92, MatA));

            // Assert
            code.Should().Be(HttpStatusCode.OK);
            texte.Should().NotContain(MatA);
            texte.Should().NotContain(MatB);
            using var json = JsonDocument.Parse(texte);
            DoitEtreSansIdentifiant(json.RootElement.GetProperty("data"), route);
        }
    }

    private static void DoitEtreSansIdentifiant(JsonElement element, string route)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject())
            {
                var nom = p.Name.ToLowerInvariant();
                (nom is "id" or "oid" or "matricule" or "matricules" or "idsal" or "nom" or "prenom" or "login")
                    .Should().BeFalse($"{route} : champ interdit « {p.Name} »");
                DoitEtreSansIdentifiant(p.Value, route);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var e in element.EnumerateArray())
                DoitEtreSansIdentifiant(e, route);
    }

    // ---------- fuite entre salariés ----------

    [Fact]
    public async Task SelfSpace_MatriculeParameterHeaderOrRouteValue_NeverExposesAnotherSalarie()
    {
        // Arrange
        using var f = Actif();
        var http = f.CreateClient();
        var supplements = new[]
        {
            $"matricule={MatB}", $"Matricule={MatB}", $"matriculeFrom={MatB}&matriculeTo={MatB}", $"cle={MatB}", $"userId={BId}"
        };

        foreach (var route in Routes)
        {
            var (_, normal) = await Appeler(http, HttpMethod.Get, Url(route), Jeton(AId, 92, MatA));
            foreach (var supplement in supplements)
            {
                // Act
                var (code, texte) = await Appeler(http, HttpMethod.Get, Url(route, supplement), Jeton(AId, 92, MatA), null,
                    ("X-Matricule", MatB), ("X-User-Id", BId.ToString(CultureInfo.InvariantCulture)));

                // Assert
                code.Should().Be(HttpStatusCode.OK);
                Donnees(texte).Should().Be(Donnees(normal));                            // la réponse de A reste la même
            }
        }
    }

    [Fact]
    public async Task SelfSpace_OtherSalarie_ReadsOwnDataDifferentFromFirst()
    {
        // Arrange
        using var f = Actif();
        var http = f.CreateClient();

        // Act
        var (_, weeks) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/weeks"), Jeton(BId, 92, MatB));
        var (_, delays) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/delays"), Jeton(BId, 92, MatB));
        var (_, anomalies) = await Appeler(http, HttpMethod.Get, Url("/api/v1/me/space/anomalies"), Jeton(BId, 92, MatB));

        // Assert
        Donnee(weeks, "semaines")[0].GetProperty("heuresSupplementaires").GetDecimal().Should().Be(25m);
        Donnee(weeks, "exoImpo")[0].GetProperty("imp130").GetDecimal().Should().Be(5m);
        Donnee(delays, "retards")[0].GetProperty("minutesRetard").GetInt32().Should().Be(45);
        Donnee(anomalies, "anomalies").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task SelfSpace_TokenMatriculeDifferentFromAccount_IsRefused()
    {
        // Arrange : le compte A est relu à chaque appel ; un jeton qui prétend porter le matricule de B alors que la base
        // dit A est refusé (403), sans aucune donnée de A ni de B.
        using var f = Actif();

        // Act
        var (code, texte) = await Appeler(f.CreateClient(), HttpMethod.Get, Url("/api/v1/me/space/weeks"), Jeton(AId, 92, MatB));

        // Assert
        code.Should().Be(HttpStatusCode.Forbidden);
        texte.Should().Contain("a changé");
        texte.Should().NotContain("semaines");
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
    public async Task SelfSpace_RefusedAccount_Gets403WithExplicitMessageOnEveryRoute(string cas, string messageAttendu)
    {
        // Arrange
        using var f = Actif();
        var http = f.CreateClient();
        var avant = _f.Snapshot();
        var (jeton, baseSage) = cas switch
        {
            "sans-matricule" => (Jeton(SansMatriculeId, 92, string.Empty), null),
            "matricule-inconnu" => (Jeton(MatriculeInconnuId, 92, "ZZZ999"), null),
            "deux-bases" => (Jeton(DeuxBasesId, 92, MatDeuxBases), null),
            "sans-permission" => (Jeton(SansPermissionId, 91, MatSansPermission), null),
            "autre-base" => (Jeton(AutreBaseId, 92, MatAutreBase), null),
            "en-tete-autre-base" => (Jeton(AId, 92, MatA), ApiFactory.OtherSageName),
            _ => (ApiFactory.TokenFor("admin")!, (string?)null)
        };

        foreach (var route in Routes)
        {
            // Act
            var (code, texte) = await Appeler(http, HttpMethod.Get, Url(route), jeton, baseSage);

            // Assert
            code.Should().Be(HttpStatusCode.Forbidden, $"{cas} {route} : attendu 403, obtenu {(int)code}");
            texte.Should().Contain(messageAttendu);
        }
        _f.Snapshot().Should().Be(avant);
    }

    [Fact]
    public async Task SelfSpace_DatabaseHeaderFromSalarie_IsRefusedUpstream()
    {
        // Arrange : TenantMiddleware exige la porte 1 dès qu'un en-tête X-Sage-Database est présent ; un salarié n'en
        // envoie jamais et la base vient alors de sa seule base autorisée : l'en-tête ne peut ni choisir ni changer la base.
        using var f = Actif();

        // Act
        var (avecEntete, _) = await Appeler(f.CreateClient(), HttpMethod.Get, Url("/api/v1/me/space/summary"),
            Jeton(AId, 92, MatA), ApiFactory.SageName);
        var (sansEntete, _) = await Appeler(f.CreateClient(), HttpMethod.Get, Url("/api/v1/me/space/summary"), Jeton(AId, 92, MatA));

        // Assert
        avecEntete.Should().Be(HttpStatusCode.Forbidden);
        sansEntete.Should().Be(HttpStatusCode.OK);
    }

    // ---------- période ----------

    public static IEnumerable<object[]> Periodes()
    {
        var aujourdhui = DateTime.Today;
        var debutMois = new DateTime(aujourdhui.Year, aujourdhui.Month, 1);
        string Q(DateTime a, DateTime b) => $"/api/v1/me/space/weeks?from={a:yyyy-MM-dd}&to={b:yyyy-MM-dd}";
        yield return new object[] { Q(debutMois.AddMonths(-4), aujourdhui), HttpStatusCode.BadRequest };            // hors fenêtre
        yield return new object[] { Q(aujourdhui, aujourdhui.AddDays(-1)), HttpStatusCode.BadRequest };              // fin avant début
        yield return new object[] { Q(aujourdhui.AddDays(1), aujourdhui.AddDays(5)), HttpStatusCode.BadRequest };     // futur
        yield return new object[] { Q(debutMois.AddMonths(-3), aujourdhui), HttpStatusCode.OK };                      // 4 mois
        yield return new object[] { "/api/v1/me/space/weeks", HttpStatusCode.OK };                                    // mois en cours
        yield return new object[] { Q(aujourdhui.AddDays(-3), aujourdhui.AddDays(30)), HttpStatusCode.OK };           // fin plafonnée
    }

    [Theory]
    [MemberData(nameof(Periodes))]
    public async Task SelfSpace_Period_IsBoundedToFourMonthsWithoutFutureDays(string url, HttpStatusCode attendu)
    {
        // Arrange
        using var f = Actif();

        // Act
        var (code, _) = await Appeler(f.CreateClient(), HttpMethod.Get, url, Jeton(AId, 92, MatA));

        // Assert
        code.Should().Be(attendu);
    }

    // ---------- un compte Salarié n'accède à aucun écran de gestion ----------

    [Theory]
    [InlineData("GET", "/api/v1/card-paie")]
    [InlineData("GET", "/api/v1/dashboard/filters")]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("GET", "/api/v1/roles")]
    [InlineData("GET", "/api/v1/closing/analysis")]
    [InlineData("GET", "/api/v1/reports/pointage")]
    [InlineData("GET", "/api/v1/anomalies")]
    [InlineData("GET", "/api/v1/overtime/exo")]
    [InlineData("GET", "/api/v1/journal")]
    [InlineData("POST", "/api/v1/sage-databases/activate")]
    public async Task ManagementRoutes_SalarieAccount_AreRefused(string methode, string url)
    {
        // Arrange
        using var f = Actif();

        // Act
        var (code, _) = await Appeler(f.CreateClient(), new HttpMethod(methode), url, Jeton(AId, 92, MatA), ApiFactory.SageName);

        // Assert
        code.Should().Be(HttpStatusCode.Forbidden, $"{methode} {url} : attendu 403, obtenu {(int)code}");
    }

    // ---------- le rôle Salarié n'obtient jamais la porte 1 ----------

    [Fact]
    public async Task EffectiveRights_SalarieWithTickedDatabaseAndSelfRead_GetsNoGateNorDatabasePermission()
    {
        // Arrange
        using var f = Actif();
        using var scope = f.Services.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IEffectiveRightsService>();

        // Act
        var droits = await sut.GetAsync(AId, fresh: true);

        // Assert
        droits.Has(PermissionCatalog.SelfRead).Should().BeTrue();
        droits.Gates.Should().BeEmpty();                                                // aucune porte, donc pas la porte 1
        droits.HasGate(1).Should().BeFalse();
        droits.Permissions.Should().NotContain(p => DatabasePermissionCodes.IsDatabaseCode(p));
        // La base cochée ne suffit pas à utiliser les services de données.
        droits.CanUseDatabase(DatabaseKind.Sage, ApiFactory.SageName).Should().BeFalse();
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;

    [Fact]
    public void SalarieRoleScript_Instructions_NeverGrantDatabasePermissionNorGate()
    {
        // Arrange : .../softtime_backend/SoftTime.Tests/Http/<ce fichier> -> .../softtime_backend/migrations
        var dossier = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..", "migrations"));
        var script = Path.Combine(dossier, "mon_espace_role_salarie.sql");
        File.Exists(script).Should().BeTrue($"script introuvable : {script}");

        // Act : seules les instructions comptent, les commentaires expliquent justement ce qu'il ne doit pas faire.
        var sql = Regex.Replace(File.ReadAllText(script), "/\\*.*?\\*/|--[^\\n]*", string.Empty, RegexOptions.Singleline);

        // Assert
        sql.Should().Contain("self.read");
        sql.Should().NotContain("'sage.");
        sql.Should().NotContain("'pointeuse.");
        sql.Should().NotContain("users.read");
        sql.Should().NotContain("T_DROIT_ROLE");    // aucune porte pour ce rôle
        sql.Should().NotContain("T_BDD_AUTORISE");  // aucun accès base créé par le script
    }

    /// <summary>Champ <c>data</c> de l'enveloppe, sans l'horodatage qui change à chaque réponse.</summary>
    private static string Donnees(string texte)
    {
        using var json = JsonDocument.Parse(texte);
        return json.RootElement.GetProperty("data").GetRawText();
    }

    /// <summary>Propriété du champ <c>data</c> de l'enveloppe (copiée : le document est libéré).</summary>
    private static JsonElement Donnee(string texte, string propriete)
    {
        using var json = JsonDocument.Parse(texte);
        return json.RootElement.GetProperty("data").GetProperty(propriete).Clone();
    }
}
