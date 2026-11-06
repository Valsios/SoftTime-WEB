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
            var users = factory.Store.GetRepo<T_RESPONSABLE>().Items;
            if (users.Any(u => u.IDRESPONSABLE == SansBaseId))
                return;
            foreach (var (id, login) in new[]
                     {
                         (SansBaseId, "audit-sans-base"), (BaseCocheeId, "audit-base-cochee"), (Porte1Id, "audit-porte1"),
                         (Porte2Id, "audit-porte2"), (Porte3Id, "audit-porte3"), (Porte6Id, "audit-porte6")
                     })
                users.Add(new T_RESPONSABLE { IDRESPONSABLE = id, IDROLE = 91, LOGIN = login, NOMRESPONSABLE = "audit" });
            // Base cochée dans « Accès bases », sans aucune permission : une permission SAGE: ou POINTEUSE: donnerait
            // la porte 1 (PermissionCatalog.DeduirePortes).
            factory.Store.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = (int)BaseCocheeId, IDRESPONSABLE = BaseCocheeId, IDBDD = 3033 });
        }
    }

    private const string Controle = "403";
    private const string ListeVide = "vide";

    public static IEnumerable<object[]> Endpoints()
    {
        var routes = new (string Methode, string Url, string? Corps, string Attendu)[]
        {
            // CardPaieController
            ("GET", "/api/cardpaie", null, Controle),
            ("GET", "/api/cardpaie/paged?page=1&pageSize=10", null, Controle),
            ("POST", "/api/cardpaie", "{}", Controle),
            ("PUT", "/api/cardpaie/1", "{}", Controle),
            ("DELETE", "/api/cardpaie/1", null, Controle),
            ("DELETE", "/api/cardpaie/by-correspondence", null, Controle),
            ("POST", "/api/cardpaie/auto-map", null, Controle),
            // DashboardController
            ("GET", "/api/dashboard/stats?from=2025-11-01&to=2025-11-30", null, Controle),
            ("GET", "/api/dashboard/filters", null, Controle),
            // SageDatabasesController
            ("GET", "/api/sage-databases", null, ListeVide),
            ("GET", "/api/sage-databases/paged?page=1&pageSize=10", null, ListeVide),
            ("POST", "/api/sage-databases", "{}", Controle),
            ("PUT", "/api/sage-databases/3033", "{}", Controle),
            ("DELETE", "/api/sage-databases/3035", null, Controle),
            ("POST", "/api/sage-databases/test-connection", "{}", Controle),
            ("POST", "/api/sage-databases/activate", null, Controle),
            ("GET", "/api/sage-databases/lookup/AUDIT", null, Controle),
            ("POST", "/api/sage-databases/lookup-batch", "[\"AUDIT\"]", Controle),
            ("GET", "/api/sage-databases/departement-service-options", null, Controle),
            // PointeuseDatabasesController
            ("GET", "/api/pointeuse-databases", null, ListeVide),
            ("GET", "/api/pointeuse-databases/paged?page=1&pageSize=10", null, ListeVide),
            ("POST", "/api/pointeuse-databases", "{}", Controle),
            ("PUT", "/api/pointeuse-databases/1", "{}", Controle),
            ("DELETE", "/api/pointeuse-databases/2", null, Controle),
            ("POST", "/api/pointeuse-databases/test-connection", "{}", Controle),
            // DepartementServiceController (porte 2) : même service que lookup
            ("GET", "/api/departement-service/lookup/AUDIT", null, Controle),
            ("POST", "/api/departement-service/lookup-batch", "[\"AUDIT\"]", Controle),
            // FieldRolesController
            ("GET", "/api/field-roles", null, Controle),
            // ImportTemplatesController
            ("GET", "/api/import-templates", null, Controle),
            // ExcelExportController
            ("POST", "/api/excel/export", "{\"fileName\":\"a.xlsx\",\"sheetName\":\"A\",\"headers\":[\"x\"],\"rows\":[[\"1\"]]}", Controle),
            ("POST", "/api/excel/export-workbook", "{\"fileName\":\"a.xlsx\",\"sheets\":[]}", Controle),
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

    private static void AssertVide(string url, string texte)
    {
        using var json = JsonDocument.Parse(texte);
        var racine = json.RootElement;
        if (url.Contains("/paged"))
        {
            Assert.Equal(0, racine.GetProperty("items").GetArrayLength());
            Assert.Equal(0, racine.GetProperty("total").GetInt32());
            Assert.Equal(1, racine.GetProperty("page").GetInt32());
            Assert.Equal(1, racine.GetProperty("totalPages").GetInt32());
        }
        else
        {
            Assert.Equal(JsonValueKind.Array, racine.ValueKind);
            Assert.Equal(0, racine.GetArrayLength());
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Account_without_any_gate_is_refused_or_gets_an_empty_list(string profil, string methode, string url, string corps, string attendu)
    {
        var avant = _f.Snapshot();

        var (code, texte) = await Appeler(profil, methode, url, corps);

        if (attendu == ListeVide)
        {
            Assert.True(code == HttpStatusCode.OK, $"{profil} {methode} {url} : attendu 200 vide, obtenu {(int)code}");
            AssertVide(url, texte);
        }
        else
            Assert.True(code == HttpStatusCode.Forbidden, $"{profil} {methode} {url} : attendu 403, obtenu {(int)code}");
        Assert.Equal(avant, _f.Snapshot());
    }

    public static IEnumerable<object[]> ListesDeBases()
    {
        foreach (var url in new[]
                 {
                     "/api/sage-databases", "/api/sage-databases/paged?page=1&pageSize=10",
                     "/api/pointeuse-databases", "/api/pointeuse-databases/paged?page=1&pageSize=10"
                 })
            yield return new object[] { url };
    }

    [Theory]
    [MemberData(nameof(ListesDeBases))]
    public async Task Gate_1_gate_2_alone_and_admin_get_the_same_complete_list_as_before(string url)
    {
        var (codeAdmin, admin) = await Appeler("admin", "GET", url);
        var (code1, porte1) = await Appeler("porte1", "GET", url);
        var (code2, porte2) = await Appeler("porte2", "GET", url);

        Assert.Equal(HttpStatusCode.OK, codeAdmin);
        Assert.Equal(HttpStatusCode.OK, code1);
        Assert.Equal(HttpStatusCode.OK, code2);
        // Même chemin de code qu'avant la correction : liste complète, identique octet pour octet pour les trois profils.
        Assert.Equal(admin, porte1);
        Assert.Equal(admin, porte2);
        using var json = JsonDocument.Parse(admin);
        var elements = url.Contains("/paged") ? json.RootElement.GetProperty("items") : json.RootElement;
        Assert.True(elements.GetArrayLength() > 0, "la liste complète ne doit pas être vide");
        Assert.DoesNotContain(ApiFactory.FakeSagePassword, admin);
        Assert.DoesNotContain(ApiFactory.FakePointeusePassword, admin);
    }

    [Theory]
    [MemberData(nameof(ListesDeBases))]
    public async Task Gate_3_alone_and_gate_6_alone_get_an_empty_list_with_200_so_the_top_bar_shows_no_error(string url)
    {
        foreach (var profil in new[] { "porte3", "porte6" })
        {
            var (code, texte) = await Appeler(profil, "GET", url);

            Assert.True(code == HttpStatusCode.OK, $"{profil} {url} : attendu 200, obtenu {(int)code}");
            AssertVide(url, texte);
        }
    }

    [Theory]
    [InlineData("GET", "/api/sage-databases/lookup/AUDIT", "")]
    [InlineData("POST", "/api/sage-databases/lookup-batch", "[\"AUDIT\"]")]
    public async Task Lookup_stays_reachable_with_gate_1_or_2_and_for_admin(string methode, string url, string corps)
    {
        foreach (var profil in new[] { "admin", "porte1", "porte2" })
        {
            var (code, _) = await Appeler(profil, methode, url, corps);

            Assert.True(code != HttpStatusCode.Forbidden && code != HttpStatusCode.Unauthorized,
                $"{profil} {methode} {url} : refusé à tort ({(int)code})");
        }
    }

    [Theory]
    [InlineData("GET", "/api/sage-databases/lookup/AUDIT", "")]
    [InlineData("POST", "/api/sage-databases/lookup-batch", "[\"AUDIT\"]")]
    public async Task Gate_1_and_gate_2_without_a_database_header_still_get_400_on_lookup_as_before(string methode, string url, string corps)
    {
        foreach (var profil in new[] { "porte1", "porte2" })
        {
            var req = new HttpRequestMessage(new HttpMethod(methode), url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Jeton(profil)}");
            if (corps.Length > 0)
                req.Content = new StringContent(corps, Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            var texte = await resp.Content.ReadAsStringAsync();

            Assert.True(resp.StatusCode == HttpStatusCode.BadRequest, $"{profil} {methode} {url} : attendu 400, obtenu {(int)resp.StatusCode}");
            Assert.Contains("X-Sage-Database", texte);
        }
    }
}
