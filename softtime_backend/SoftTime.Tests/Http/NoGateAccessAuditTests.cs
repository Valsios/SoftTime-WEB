using System.Net;
using System.Text;
using System.Text.Json;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Audit des contrôleurs qui n'ont pas de porte au niveau de la classe : un compte authentifié SANS AUCUNE PORTE ne doit
/// lire aucune donnée ni rien modifier. Deux profils sans porte : sans base, et avec la base cochée dans « Accès bases » mais
/// sans permission de lecture (cas prévu du salarié).
/// <list type="bullet">
///   <item>403 sur toutes les routes de contrôle, y compris lookup et lookup-batch (département et service lus dans SAGE) ;</item>
///   <item>les quatre listes de bases (GET et GET paged de SageDatabases et de PointeuseDatabases) répondent 200 VIDE, car elles
///     servent aux écrans des portes 1 et 2 et à la barre du haut de tout compte ;</item>
///   <item>porte 1, porte 2 seule et administrateur reçoivent les mêmes listes complètes qu'avant ; porte 3 seule et porte 6 seule
///     (jamais de porte 1 ni 2) reçoivent des listes vides, en 200 : la barre du haut ne reçoit aucune erreur.</item>
/// </list>
/// </summary>
public class NoGateAccessAuditTests : IClassFixture<ApiFactory>
{
    private const decimal SansBaseId = 10097;
    private const decimal BaseCocheeId = 10098;
    private const decimal Porte1Id = 10101;
    private const decimal Porte2Id = 10102;
    private const decimal Porte3Id = 10103;
    private const decimal Porte6Id = 10104;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public NoGateAccessAuditTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (factory.Store)
        {
            var users = factory.Store.Get<Responsable>().Items;
            if (users.Any(u => u.IdResponsable == SansBaseId))
                return;
            foreach (var (id, login) in new[]
                     {
                         (SansBaseId, "audit-sans-base"), (BaseCocheeId, "audit-base-cochee"), (Porte1Id, "audit-porte1"),
                         (Porte2Id, "audit-porte2"), (Porte3Id, "audit-porte3"), (Porte6Id, "audit-porte6")
                     })
                users.Add(new Responsable { IdResponsable = id, IdRole = 91, Login = login, NomResponsable = "audit" });
            // Base cochée dans « Accès bases », sans aucune permission : une permission SAGE: ou POINTEUSE: donnerait
            // la porte 1 (PermissionCatalog.DeduirePortes).
            factory.Store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = (int)BaseCocheeId, IdResponsable = BaseCocheeId, IdBdd = 3033 });
        }
    }

    private const string Controle = "403";
    private const string ListeVide = "vide";

    public static IEnumerable<object[]> Endpoints()
    {
        var routes = new (string Methode, string Url, string? Corps, string Attendu)[]
        {
            // CardPaieController
            ("GET", "/api/v1/card-paie", null, Controle),
            ("GET", "/api/v1/card-paie/paged?page=1&pageSize=10", null, Controle),
            ("POST", "/api/v1/card-paie", "{}", Controle),
            ("PUT", "/api/v1/card-paie/1", "{}", Controle),
            ("DELETE", "/api/v1/card-paie/1", null, Controle),
            ("DELETE", "/api/v1/card-paie/by-correspondence", null, Controle),
            ("POST", "/api/v1/card-paie/auto-map", null, Controle),
            // DashboardController
            ("GET", "/api/v1/dashboard/stats?from=2025-11-01&to=2025-11-30", null, Controle),
            ("GET", "/api/v1/dashboard/filters", null, Controle),
            // SageDatabasesController
            ("GET", "/api/v1/sage-databases", null, ListeVide),
            ("GET", "/api/v1/sage-databases/paged?page=1&pageSize=10", null, ListeVide),
            ("POST", "/api/v1/sage-databases", "{}", Controle),
            ("PUT", "/api/v1/sage-databases/3033", "{}", Controle),
            ("DELETE", "/api/v1/sage-databases/3035", null, Controle),
            ("POST", "/api/v1/sage-databases/test-connection", "{}", Controle),
            ("POST", "/api/v1/sage-databases/activate", null, Controle),
            ("GET", "/api/v1/sage-databases/lookup/AUDIT", null, Controle),
            ("POST", "/api/v1/sage-databases/lookup-batch", "[\"AUDIT\"]", Controle),
            ("GET", "/api/v1/sage-databases/departement-service-options", null, Controle),
            // PointeuseDatabasesController
            ("GET", "/api/v1/pointeuse-databases", null, ListeVide),
            ("GET", "/api/v1/pointeuse-databases/paged?page=1&pageSize=10", null, ListeVide),
            ("POST", "/api/v1/pointeuse-databases", "{}", Controle),
            ("PUT", "/api/v1/pointeuse-databases/1", "{}", Controle),
            ("DELETE", "/api/v1/pointeuse-databases/2", null, Controle),
            ("POST", "/api/v1/pointeuse-databases/test-connection", "{}", Controle),
            // DepartementServiceController (porte 2) : même service que lookup
            ("GET", "/api/v1/departement-service/lookup/AUDIT", null, Controle),
            ("POST", "/api/v1/departement-service/lookup-batch", "[\"AUDIT\"]", Controle),
            // FieldRolesController
            ("GET", "/api/v1/field-roles", null, Controle),
            // ImportTemplatesController
            ("GET", "/api/v1/import-templates", null, Controle),
            // ExcelExportController
            ("POST", "/api/v1/excel/export", "{\"fileName\":\"a.xlsx\",\"sheetName\":\"A\",\"headers\":[\"x\"],\"rows\":[[\"1\"]]}", Controle),
            ("POST", "/api/v1/excel/export-workbook", "{\"fileName\":\"a.xlsx\",\"sheets\":[]}", Controle),
        };
        foreach (var profil in new[] { "sans-base", "base-cochee" })
            foreach (var (m, u, c, a) in routes)
                yield return new object[] { profil, m, u, c ?? string.Empty, a };
    }

    private static string? Jeton(string profil) => profil switch
    {
        "sans-base" => ApiFactory.Token(SansBaseId, 91),
        "base-cochee" => ApiFactory.Token(BaseCocheeId, 91),
        "porte1" => ApiFactory.Token(Porte1Id, 91, 1),
        "porte2" => ApiFactory.Token(Porte2Id, 91, 2),
        "porte3" => ApiFactory.Token(Porte3Id, 91, 3),
        "porte6" => ApiFactory.Token(Porte6Id, 91, 6),
        "admin" => ApiFactory.TokenFor("admin"),
        _ => null
    };

    private async Task<(HttpStatusCode Code, string Texte)> Appeler(string profil, string methode, string url, string corps = "")
    {
        var req = new HttpRequestMessage(new HttpMethod(methode), url);
        if (Jeton(profil) is string t)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {t}");
        req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
        req.Headers.TryAddWithoutValidation("X-Pointeuse-Database", "PTG");
        if (corps.Length > 0)
            req.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    /// <summary>Champ <c>data</c> de l'enveloppe, sans l'horodatage qui change à chaque réponse.</summary>
    private static string Donnees(string texte)
    {
        using var json = JsonDocument.Parse(texte);
        return json.RootElement.GetProperty("data").GetRawText();
    }

    private static void DoitEtreVide(string url, string texte)
    {
        using var json = JsonDocument.Parse(texte);
        var donnees = json.RootElement.GetProperty("data");
        if (url.Contains("/paged"))
        {
            donnees.GetProperty("items").GetArrayLength().Should().Be(0);
            donnees.GetProperty("total").GetInt32().Should().Be(0);
            donnees.GetProperty("page").GetInt32().Should().Be(1);
            donnees.GetProperty("totalPages").GetInt32().Should().Be(1);
        }
        else
        {
            donnees.ValueKind.Should().Be(JsonValueKind.Array);
            donnees.GetArrayLength().Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ControlRoutes_AccountWithoutAnyGate_AreRefusedOrGiveEmptyList(
        string profil, string methode, string url, string corps, string attendu)
    {
        // Arrange
        var avant = _f.Snapshot();

        // Act
        var (code, texte) = await Appeler(profil, methode, url, corps);

        // Assert
        if (attendu == ListeVide)
        {
            code.Should().Be(HttpStatusCode.OK, $"{profil} {methode} {url} : attendu 200 vide, obtenu {(int)code}");
            DoitEtreVide(url, texte);
        }
        else
            code.Should().Be(HttpStatusCode.Forbidden, $"{profil} {methode} {url} : attendu 403, obtenu {(int)code}");
        _f.Snapshot().Should().Be(avant);
    }

    public static IEnumerable<object[]> ListesDeBases()
    {
        foreach (var url in new[]
                 {
                     "/api/v1/sage-databases", "/api/v1/sage-databases/paged?page=1&pageSize=10",
                     "/api/v1/pointeuse-databases", "/api/v1/pointeuse-databases/paged?page=1&pageSize=10"
                 })
            yield return new object[] { url };
    }

    [Theory]
    [MemberData(nameof(ListesDeBases))]
    public async Task DatabaseLists_Gate1Gate2AndAdmin_GetSameCompleteList(string url)
    {
        // Act
        var (codeAdmin, admin) = await Appeler("admin", "GET", url);
        var (code1, porte1) = await Appeler("porte1", "GET", url);
        var (code2, porte2) = await Appeler("porte2", "GET", url);

        // Assert
        codeAdmin.Should().Be(HttpStatusCode.OK);
        code1.Should().Be(HttpStatusCode.OK);
        code2.Should().Be(HttpStatusCode.OK);
        // Même chemin de code qu'avant la correction : liste complète, identique pour les trois profils.
        Donnees(porte1).Should().Be(Donnees(admin));
        Donnees(porte2).Should().Be(Donnees(admin));
        using var json = JsonDocument.Parse(admin);
        var donnees = json.RootElement.GetProperty("data");
        var elements = url.Contains("/paged") ? donnees.GetProperty("items") : donnees;
        elements.GetArrayLength().Should().BePositive("la liste complète ne doit pas être vide");
        admin.Should().NotContain(ApiFactory.FakeSagePassword);
        admin.Should().NotContain(ApiFactory.FakePointeusePassword);
    }

    [Theory]
    [MemberData(nameof(ListesDeBases))]
    public async Task DatabaseLists_Gate3OrGate6Alone_GetEmptyListWith200(string url)
    {
        foreach (var profil in new[] { "porte3", "porte6" })
        {
            // Act
            var (code, texte) = await Appeler(profil, "GET", url);

            // Assert : la barre du haut ne reçoit aucune erreur.
            code.Should().Be(HttpStatusCode.OK, $"{profil} {url} : attendu 200, obtenu {(int)code}");
            DoitEtreVide(url, texte);
        }
    }

    [Theory]
    [InlineData("GET", "/api/v1/sage-databases/lookup/AUDIT", "")]
    [InlineData("POST", "/api/v1/sage-databases/lookup-batch", "[\"AUDIT\"]")]
    public async Task Lookup_Gate1Gate2OrAdmin_StaysReachable(string methode, string url, string corps)
    {
        foreach (var profil in new[] { "admin", "porte1", "porte2" })
        {
            // Act
            var (code, _) = await Appeler(profil, methode, url, corps);

            // Assert
            code.Should().NotBe(HttpStatusCode.Forbidden, $"{profil} {methode} {url} : refusé à tort");
            code.Should().NotBe(HttpStatusCode.Unauthorized, $"{profil} {methode} {url} : refusé à tort");
        }
    }

    [Theory]
    [InlineData("GET", "/api/v1/sage-databases/lookup/AUDIT", "")]
    [InlineData("POST", "/api/v1/sage-databases/lookup-batch", "[\"AUDIT\"]")]
    public async Task Lookup_Gate1OrGate2WithoutDatabaseHeader_Gives400(string methode, string url, string corps)
    {
        foreach (var profil in new[] { "porte1", "porte2" })
        {
            // Arrange
            var req = new HttpRequestMessage(new HttpMethod(methode), url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Jeton(profil)}");
            if (corps.Length > 0)
                req.Content = new StringContent(corps, Encoding.UTF8, "application/json");

            // Act
            var resp = await _http.SendAsync(req);
            var texte = await resp.Content.ReadAsStringAsync();

            // Assert
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{profil} {methode} {url}");
            texte.Should().Contain("X-Sage-Database");
        }
    }
}
