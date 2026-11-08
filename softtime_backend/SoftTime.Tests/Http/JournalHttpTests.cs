using System.Net;
using System.Text.Json;

using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Journal central (<c>GET api/journal</c>, détail, facets) : lecture réservée à l'administrateur ou à la porte 5 avec
/// la permission TRACE:AUDIT effective, masquage à la lecture, aucune route d'écriture ; configuration des
/// déclencheurs d'audit réservée aux administrateurs.
/// </summary>
public class JournalHttpTests : IClassFixture<ApiFactory>
{
    private const string Denied = "Vous n'avez pas la permission requise pour cette consultation.";
    private const string AdminOnly = "Cette opération est réservée aux administrateurs.";
    private const string InjectedClearValue = "VALEUR-CLAIRE-INJECTEE-TEST";
    private const long ClearRowId = 900;

    private static readonly DateTime Base = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public JournalHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (factory.Store) Seed(factory);
    }

    /// <summary>Comptes propres à ces tests (rôle 91 sans droits : chaque ligne utilisateur est un ajout).</summary>
    private static string? Token(string profile) => profile switch
    {
        "tracereader" => ApiFactory.Token(10100, 91, 5),   // porte 5 + TRACE:AUDIT effective
        "gate5only" => ApiFactory.Token(10101, 91, 5),     // porte 5 dans le jeton, sans la permission effective
        _ => ApiFactory.TokenFor(profile)
    };

    private static void Seed(ApiFactory f)
    {
        var users = f.Store.GetRepo<Responsable>().Items;
        if (users.Any(u => u.IdResponsable == 10100))
            return;
        users.Add(new Responsable { IdResponsable = 10100, IdRole = 91, Login = "tracereader", NomResponsable = "tracereader" });
        users.Add(new Responsable { IdResponsable = 10101, IdRole = 91, Login = "gate5only", NomResponsable = "gate5only" });
        var perms = f.Store.GetRepo<UserPermission>().Items;
        perms.Add(new UserPermission { IdResponsable = 10100, PermissionCode = "TRACE:AUDIT", IsGranted = true });
        perms.Add(new UserPermission { IdResponsable = 10101, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        var journal = f.Store.GetRepo<JournalAudit>().Items;
        journal.Add(Row(1, Base, "T_FERIE", "Insertion", "agent1", "INTITULE", null, "Noel"));
        journal.Add(Row(2, Base.AddDays(1), "T_FERIE", "Modification", "agent2", "INTITULE", "Noel", "Noël"));
        journal.Add(Row(3, Base.AddDays(2), "T_RESPONSABLE", "Suppression", "agent1", "EMAIL", "a@exemple.test", null));
        journal.Add(Row(4, Base.AddDays(3), "T_CUMSAL", "SYNC_PAIE", "agent2", null, null, "Statut=OK"));
        // Ligne ancienne stockée en clair (avant le masquage explicite) : ne doit jamais sortir en clair.
        journal.Add(Row(ClearRowId, Base.AddDays(4), "T_BDD_SAGE", "Modification", "agent1", "TMDP", InjectedClearValue, InjectedClearValue,
            key: "ID=3033"));
        // Volume pour la pagination : 230 lignes d'une même table, une minute d'écart.
        for (var i = 0; i < 230; i++)
            journal.Add(Row(1000 + i, Base.AddMonths(-3).AddMinutes(i), "T_VOLUME", "Insertion", "volume", "COL", null, $"v{i}"));
    }

    private static JournalAudit Row(long id, DateTime date, string table, string action, string user, string? column,
        string? before, string? after, string? key = null)
        => new()
        {
            Id = id, DateUtc = date, Tables = table, Actions = action, Users = user, Colonnes = column,
            Anciens = before, Nouveaux = after, Cles = key ?? $"ID={id}", BaseSage = ApiFactory.SageName,
            Correlations = new Guid((int)id, 0, 0, new byte[8]), Source = "TEST"
        };

    private async Task<(HttpStatusCode Status, string Text)> Send(string profile, HttpMethod method, string url, string? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (Token(profile) is string t)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {t}");
        if (body is not null)
            req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    public static IEnumerable<object[]> Reads()
    {
        foreach (var url in new[] { "/api/v1/journal", $"/api/v1/journal/{ClearRowId}", "/api/v1/journal/facets" })
            foreach (var profile in new[] { "admin", "tracereader", "gate5only", "user1", "param2", "joel", "anonyme" })
                yield return new object[] { url, profile };
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Journal_read_needs_admin_or_gate_5_with_trace_audit(string url, string profile)
    {
        var before = _f.Snapshot();
        var (status, text) = await Send(profile, HttpMethod.Get, url);
        ApiFactory.Report($"JOURNAL|{url}|{profile}|{(int)status}");

        if (profile == "anonyme")
            Assert.Equal(HttpStatusCode.Unauthorized, status);
        else if (profile is "admin" or "tracereader")
            Assert.Equal(HttpStatusCode.OK, status);
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.Contains(Denied, text);
        }
        Assert.Equal(before, _f.Snapshot());   // une lecture n'écrit rien, pas même dans le journal
    }

    [Fact]
    public async Task Clear_value_stored_before_masking_never_leaves_the_api()
    {
        foreach (var url in new[] { "/api/v1/journal", $"/api/v1/journal/{ClearRowId}", "/api/v1/journal?table=T_BDD_SAGE" })
        {
            var (status, text) = await Send("tracereader", HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.DoesNotContain(InjectedClearValue, text);
        }

        var (_, detail) = await Send("admin", HttpMethod.Get, $"/api/v1/journal/{ClearRowId}");
        using var doc = JsonDocument.Parse(detail);
        Assert.Equal("TMDP", doc.RootElement.GetProperty("column").GetString());
        Assert.Equal(AuditMaskingPolicy.Mask, doc.RootElement.GetProperty("before").GetString());
        Assert.Equal(AuditMaskingPolicy.Mask, doc.RootElement.GetProperty("after").GetString());
        // Le stockage n'est pas modifié par la lecture (aucune purge implicite).
        Assert.Equal(InjectedClearValue, _f.Store.GetRepo<JournalAudit>().Items.Single(e => e.Id == ClearRowId).Nouveaux);
    }

    [Fact]
    public async Task Email_stays_visible()
    {
        var (_, text) = await Send("admin", HttpMethod.Get, "/api/v1/journal/3");
        Assert.Contains("a@exemple.test", text);
    }

    [Fact]
    public async Task Filters_on_period_user_action_and_table_apply()
    {
        var from = Base.ToString("O");
        var to = Base.AddDays(5).ToString("O");

        var period = await Page($"/api/v1/journal?from={from}&to={to}");
        Assert.Equal(5, period.Total);
        Assert.Equal(new long[] { ClearRowId, 4, 3, 2, 1 }, period.Ids);   // DATE_UTC décroissante

        Assert.Equal(new long[] { ClearRowId, 3, 1 }, (await Page($"/api/v1/journal?from={from}&to={to}&user=agent1")).Ids);
        Assert.Equal(new long[] { 4 }, (await Page($"/api/v1/journal?from={from}&action=SYNC_PAIE")).Ids);
        Assert.Equal(new long[] { 2, 1 }, (await Page($"/api/v1/journal?from={from}&table=T_FERIE")).Ids);
        Assert.Equal(new long[] { 2 }, (await Page($"/api/v1/journal?from={from}&table=T_FERIE&action=Modification")).Ids);
        Assert.Equal(new long[] { 3 }, (await Page("/api/v1/journal?key=ID%3D3")).Ids);
        Assert.Equal(new long[] { 2 }, (await Page($"/api/v1/journal?correlation={new Guid(2, 0, 0, new byte[8])}")).Ids);
        Assert.Equal(0, (await Page($"/api/v1/journal?sageDatabase={ApiFactory.OtherSageName}")).Total);
    }

    [Fact]
    public async Task Pagination_defaults_to_50_and_caps_the_page_size_at_200()
    {
        var first = await Page("/api/v1/journal?table=T_VOLUME");
        Assert.Equal(230, first.Total);
        Assert.Equal(50, first.PageSize);
        Assert.Equal(50, first.Ids.Length);
        Assert.Equal(1229, first.Ids[0]);

        var capped = await Page("/api/v1/journal?table=T_VOLUME&pageSize=5000");
        Assert.Equal(200, capped.PageSize);
        Assert.Equal(200, capped.Ids.Length);

        var last = await Page("/api/v1/journal?table=T_VOLUME&page=2&pageSize=200");
        Assert.Equal(30, last.Ids.Length);
        Assert.Equal(1000, last.Ids[^1]);

        Assert.Empty((await Page("/api/v1/journal?table=T_VOLUME&page=99&pageSize=200")).Ids);
    }

    [Fact]
    public async Task Facets_list_distinct_values_of_the_period()
    {
        var (status, text) = await Send("tracereader", HttpMethod.Get,
            $"/api/v1/journal/facets?from={Base:O}&to={Base.AddDays(5):O}");
        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(text);
        // L'ordre suit la collation de la source : on compare les ensembles.
        string[] Values(string name) => doc.RootElement.GetProperty(name).EnumerateArray()
            .Select(v => v.GetString()!).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "T_BDD_SAGE", "T_CUMSAL", "T_FERIE", "T_RESPONSABLE" }, Values("tables"));
        Assert.Equal(new[] { "Insertion", "Modification", "SYNC_PAIE", "Suppression" }, Values("actions"));
        Assert.Equal(new[] { "agent1", "agent2" }, Values("users"));
    }

    [Fact]
    public async Task Unknown_journal_line_is_404_for_an_authorized_reader()
    {
        var (status, _) = await Send("admin", HttpMethod.Get, "/api/v1/journal/424242");
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Theory]
    [InlineData("POST", "/api/v1/journal")]
    [InlineData("PUT", "/api/v1/journal")]
    [InlineData("DELETE", "/api/v1/journal")]
    [InlineData("PATCH", "/api/v1/journal")]
    [InlineData("PUT", "/api/v1/journal/1")]
    [InlineData("DELETE", "/api/v1/journal/1")]
    [InlineData("PATCH", "/api/v1/journal/1")]
    [InlineData("POST", "/api/v1/journal/facets")]
    public async Task Journal_has_no_write_route(string method, string url)
    {
        var before = _f.Snapshot();
        var (status, _) = await Send("admin", new HttpMethod(method), url, "{}");
        ApiFactory.Report($"JOURNAL-WRITE|{method} {url}|admin|{(int)status}");
        Assert.Contains(status, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(before, _f.Snapshot());
    }

    public static IEnumerable<object[]> ConfigWrites()
    {
        foreach (var (method, url) in new[] { ("POST", "/api/v1/audit/config"), ("PUT", "/api/v1/audit/config/1") })
            foreach (var profile in new[] { "tracereader", "gate5only", "user1", "param2", "joel", "anonyme" })
                yield return new object[] { method, url, profile };
    }

    [Theory]
    [MemberData(nameof(ConfigWrites))]
    public async Task Audit_configuration_writes_are_refused_to_non_administrators(string method, string url, string profile)
    {
        var before = _f.Snapshot();
        var (status, text) = await Send(profile, new HttpMethod(method), url, ConfigBody);
        ApiFactory.Report($"AUDIT-CONFIG|{method} {url}|{profile}|{(int)status}");

        if (profile == "anonyme")
            Assert.Equal(HttpStatusCode.Unauthorized, status);
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, status);
            // Avec la porte 5 dans le jeton, c'est la règle administrateur qui refuse (sans porte 5, le filtre de porte refuse avant).
            if (profile is "tracereader" or "gate5only") Assert.Contains(AdminOnly, text);
        }
        Assert.Equal(before, _f.Snapshot());
    }

    [Fact]
    public async Task Administrator_can_create_audit_configuration()
    {
        var (status, _) = await Send("admin", HttpMethod.Post, "/api/v1/audit/config", ConfigBody);
        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Contains(_f.Store.GetRepo<SuiviConfiguration>().Items, c => c.Tables == "T_JOURNAL_TEST");
    }

    [Fact]
    public async Task Administrator_update_of_audit_configuration_passes_authorization()
    {
        // Le double en mémoire ne retrouve pas suivi_TBLCLN par identifiant : 404 attendu, jamais 401 ni 403.
        var (status, text) = await Send("admin", HttpMethod.Put, "/api/v1/audit/config/1", ConfigBody);
        ApiFactory.Report($"AUDIT-CONFIG|PUT /api/v1/audit/config/1|admin|{(int)status}");
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain(AdminOnly, text);
    }

    private const string ConfigBody =
        "{\"id\":0,\"table\":\"T_JOURNAL_TEST\",\"column\":\"COL\",\"type\":\"varchar\",\"enabled\":true,\"base\":\"SFOI_PAIE2025\"}";

    private async Task<(int Total, int PageSize, long[] Ids)> Page(string url)
    {
        var (status, text) = await Send("admin", HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return (root.GetProperty("total").GetInt32(), root.GetProperty("pageSize").GetInt32(),
            root.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToArray());
    }
}
