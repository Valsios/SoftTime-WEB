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
        var users = f.Store.Get<Responsable>().Items;
        if (users.Any(u => u.IdResponsable == 10100))
            return;
        users.Add(new Responsable { IdResponsable = 10100, IdRole = 91, Login = "tracereader", NomResponsable = "tracereader" });
        users.Add(new Responsable { IdResponsable = 10101, IdRole = 91, Login = "gate5only", NomResponsable = "gate5only" });
        var perms = f.Store.Get<UserPermission>().Items;
        perms.Add(new UserPermission { IdResponsable = 10100, PermissionCode = "TRACE:AUDIT", IsGranted = true });
        perms.Add(new UserPermission { IdResponsable = 10101, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        var journal = f.Store.Get<JournalAudit>().Items;
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
    public async Task GetJournal_WithoutAdminOrGate5TraceAudit_IsRefused(string url, string profile)
    {
        // Arrange
        var before = _f.Snapshot();
        var (status, text) = await Send(profile, HttpMethod.Get, url);

        // Act
        ApiFactory.Report($"JOURNAL|{url}|{profile}|{(int)status}");

        // Assert
        if (profile == "anonyme")
            status.Should().Be(HttpStatusCode.Unauthorized);
        else if (profile is "admin" or "tracereader")
            status.Should().Be(HttpStatusCode.OK);
        else
        {
            status.Should().Be(HttpStatusCode.Forbidden);
            text.Should().Contain(Denied);
        }
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task GetJournal_ClearValueStoredBeforeMasking_IsNeverReturned()
    {
        foreach (var url in new[] { "/api/v1/journal", $"/api/v1/journal/{ClearRowId}", "/api/v1/journal?table=T_BDD_SAGE" })
        {
            var (status, text) = await Send("tracereader", HttpMethod.Get, url);
            status.Should().Be(HttpStatusCode.OK);
            text.Should().NotContain(InjectedClearValue);
        }

        var (_, detail) = await Send("admin", HttpMethod.Get, $"/api/v1/journal/{ClearRowId}");
        using var doc = JsonDocument.Parse(detail);
        doc.RootElement.GetProperty("data").GetProperty("column").GetString().Should().Be("TMDP");
        doc.RootElement.GetProperty("data").GetProperty("before").GetString().Should().Be(AuditMaskingPolicy.Mask);
        doc.RootElement.GetProperty("data").GetProperty("after").GetString().Should().Be(AuditMaskingPolicy.Mask);
        // Le stockage n'est pas modifié par la lecture (aucune purge implicite).
        _f.Store.Get<JournalAudit>().Items.Single(e => e.Id == ClearRowId).Nouveaux.Should().Be(InjectedClearValue);
    }

    [Fact]
    public async Task GetJournal_EmailColumn_StaysVisible()
    {
        // Act
        var (_, text) = await Send("admin", HttpMethod.Get, "/api/v1/journal/3");

        // Assert
        text.Should().Contain("a@exemple.test");
    }

    [Fact]
    public async Task GetJournal_PeriodUserActionAndTableFilters_Apply()
    {
        // Arrange
        var from = Base.ToString("O");
        var to = Base.AddDays(5).ToString("O");

        // Act
        var period = await Page($"/api/v1/journal?from={from}&to={to}");

        // Assert
        period.Total.Should().Be(5);
        period.Ids.Should().Equal(new long[] { ClearRowId, 4, 3, 2, 1 });
        (await Page($"/api/v1/journal?from={from}&to={to}&user=agent1")).Ids.Should().Equal(new long[] { ClearRowId, 3, 1 });
        (await Page($"/api/v1/journal?from={from}&action=SYNC_PAIE")).Ids.Should().Equal(new long[] { 4 });
        (await Page($"/api/v1/journal?from={from}&table=T_FERIE")).Ids.Should().Equal(new long[] { 2, 1 });
        (await Page($"/api/v1/journal?from={from}&table=T_FERIE&action=Modification")).Ids.Should().Equal(new long[] { 2 });
        (await Page("/api/v1/journal?key=ID%3D3")).Ids.Should().Equal(new long[] { 3 });
        (await Page($"/api/v1/journal?correlation={new Guid(2, 0, 0, new byte[8])}")).Ids.Should().Equal(new long[] { 2 });
        (await Page($"/api/v1/journal?sageDatabase={ApiFactory.OtherSageName}")).Total.Should().Be(0);
    }

    [Fact]
    public async Task GetJournal_Pagination_DefaultsTo50AndCapsAt200()
    {
        // Act
        var first = await Page("/api/v1/journal?table=T_VOLUME");

        // Assert
        first.Total.Should().Be(230);
        first.PageSize.Should().Be(50);
        first.Ids.Length.Should().Be(50);
        first.Ids[0].Should().Be(1229);
        var capped = await Page("/api/v1/journal?table=T_VOLUME&pageSize=5000");
        capped.PageSize.Should().Be(200);
        capped.Ids.Length.Should().Be(200);
        var last = await Page("/api/v1/journal?table=T_VOLUME&page=2&pageSize=200");
        last.Ids.Length.Should().Be(30);
        last.Ids[^1].Should().Be(1000);
        (await Page("/api/v1/journal?table=T_VOLUME&page=99&pageSize=200")).Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFacets_Period_ListsDistinctValues()
    {
        // Act
        var (status, text) = await Send("tracereader", HttpMethod.Get,
            $"/api/v1/journal/facets?from={Base:O}&to={Base.AddDays(5):O}");

        // Assert
        status.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(text);
        // L'ordre suit la collation de la source : on compare les ensembles.
        string[] Values(string name) => doc.RootElement.GetProperty("data").GetProperty(name).EnumerateArray()
            .Select(v => v.GetString()!).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Values("tables").Should().Equal(new[] { "T_BDD_SAGE", "T_CUMSAL", "T_FERIE", "T_RESPONSABLE" });
        Values("actions").Should().Equal(new[] { "Insertion", "Modification", "SYNC_PAIE", "Suppression" });
        Values("users").Should().Equal(new[] { "agent1", "agent2" });
    }

    [Fact]
    public async Task GetJournalLine_UnknownLine_Returns404()
    {
        // Act
        var (status, _) = await Send("admin", HttpMethod.Get, "/api/v1/journal/424242");

        // Assert
        status.Should().Be(HttpStatusCode.NotFound);
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
    public async Task Journal_WriteMethods_HaveNoRoute(string method, string url)
    {
        // Arrange
        var before = _f.Snapshot();
        var (status, _) = await Send("admin", new HttpMethod(method), url, "{}");

        // Act
        ApiFactory.Report($"JOURNAL-WRITE|{method} {url}|admin|{(int)status}");

        // Assert
        new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed }.Should().Contain(status);
        _f.Snapshot().Should().Be(before);
    }

    public static IEnumerable<object[]> ConfigWrites()
    {
        foreach (var (method, url) in new[] { ("POST", "/api/v1/audit/config"), ("PUT", "/api/v1/audit/config/1") })
            foreach (var profile in new[] { "tracereader", "gate5only", "user1", "param2", "joel", "anonyme" })
                yield return new object[] { method, url, profile };
    }

    [Theory]
    [MemberData(nameof(ConfigWrites))]
    public async Task AuditConfiguration_NonAdministratorWrites_AreRefused(string method, string url, string profile)
    {
        // Arrange
        var before = _f.Snapshot();
        var (status, text) = await Send(profile, new HttpMethod(method), url, ConfigBody);

        // Act
        ApiFactory.Report($"AUDIT-CONFIG|{method} {url}|{profile}|{(int)status}");

        // Assert
        if (profile == "anonyme")
            status.Should().Be(HttpStatusCode.Unauthorized);
        else
        {
            status.Should().Be(HttpStatusCode.Forbidden);
            // Avec la porte 5 et TRACE:AUDIT, c'est la règle administrateur qui refuse. Sans TRACE:AUDIT effectif (gate5only),
            // la permission fine de l'écran refuse avant (lot 4c) ; sans porte 5, le filtre de porte refuse avant.
            if (profile == "tracereader") text.Should().Contain(AdminOnly);
            if (profile == "gate5only") text.Should().Contain("Vous n'avez pas la permission requise pour cette action.");
        }
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task AuditConfiguration_AdministratorCreate_Succeeds()
    {
        // Act
        var (status, _) = await Send("admin", HttpMethod.Post, "/api/v1/audit/config", ConfigBody);

        // Assert
        status.Should().Be(HttpStatusCode.NoContent);
        _f.Store.Get<SuiviConfiguration>().Items.Should().Contain(c => c.Tables == "T_JOURNAL_TEST");
    }

    [Fact]
    public async Task AuditConfiguration_AdministratorUpdate_PassesAuthorization()
    {
        // Arrange
        // Le double en mémoire ne retrouve pas suivi_TBLCLN par identifiant : 404 attendu, jamais 401 ni 403.
        var (status, text) = await Send("admin", HttpMethod.Put, "/api/v1/audit/config/1", ConfigBody);

        // Act
        ApiFactory.Report($"AUDIT-CONFIG|PUT /api/v1/audit/config/1|admin|{(int)status}");

        // Assert
        status.Should().NotBe(HttpStatusCode.Unauthorized);
        status.Should().NotBe(HttpStatusCode.Forbidden);
        text.Should().NotContain(AdminOnly);
    }

    private const string ConfigBody =
        "{\"id\":0,\"table\":\"T_JOURNAL_TEST\",\"column\":\"COL\",\"type\":\"varchar\",\"enabled\":true,\"base\":\"SFOI_PAIE2025\"}";

    private async Task<(int Total, int PageSize, long[] Ids)> Page(string url)
    {
        var (status, text) = await Send("admin", HttpMethod.Get, url);
        status.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement.GetProperty("data");
        return (root.GetProperty("total").GetInt32(), root.GetProperty("pageSize").GetInt32(),
            root.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToArray());
    }
}
