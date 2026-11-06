using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Étape 4b-8 : changer le rôle d'un compte qui a des ajustements personnels demande une confirmation ;
/// la confirmation efface les ajustements dans la même sauvegarde.
/// </summary>
public class RoleChangeHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public RoleChangeHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    private static string Put(int id, int roleId, string login, bool confirm = false)
        => $$"""{"id":{{id}},"roleId":{{roleId}},"nom":"{{login}}","login":"{{login}}","confirmClearAdjustments":{{(confirm ? "true" : "false")}}}""";

    private async Task<(HttpResponseMessage Resp, string Body)> Send(string profile, HttpMethod method, string url, string? body = null)
    {
        var resp = await _http.SendAsync(_f.Request(profile, method, url, body));
        return (resp, await resp.Content.ReadAsStringAsync());
    }

    private int Rows(decimal id) => _f.Store.GetRepo<T_USER_PERMISSION>().Items.Count(p => p.IDRESPONSABLE == id);

    [Fact]
    public async Task Preview_for_an_administrator_lists_the_adjustments_that_would_be_cleared()
    {
        var before = _f.Snapshot();
        var (resp, text) = await Send("admin", HttpMethod.Post, "/api/users/10080/role-change-preview", """{"newRoleId":40}""");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("adjusted1", doc.RootElement.GetProperty("accountLogin").GetString());
        var codes = doc.RootElement.GetProperty("adjustmentsToClear").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "+PROC:PUNCHES", "+PROC:SHIFTS" }, codes.OrderBy(c => c));
        Assert.Equal(before, _f.Snapshot());                 // l'aperçu n'écrit rien
    }

    [Theory]
    [InlineData("param2")]
    [InlineData("usersreader")]
    [InlineData("joel")]
    [InlineData("user1")]
    public async Task Preview_is_refused_to_non_administrators(string profile)
    {
        var (resp, _) = await Send(profile, HttpMethod.Post, "/api/users/10080/role-change-preview", """{"newRoleId":40}""");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Preview_is_refused_to_anonymous()
    {
        var (resp, _) = await Send("anonyme", HttpMethod.Post, "/api/users/10080/role-change-preview", """{"newRoleId":40}""");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Preview_has_count_zero_without_adjustments_or_when_the_role_does_not_change()
    {
        var (withoutAdjustments, text1) = await Send("admin", HttpMethod.Post, "/api/users/10050/role-change-preview", """{"newRoleId":40}""");
        Assert.Equal(HttpStatusCode.OK, withoutAdjustments.StatusCode);
        Assert.Equal(0, JsonDocument.Parse(text1).RootElement.GetProperty("count").GetInt32());

        var (sameRole, text2) = await Send("admin", HttpMethod.Post, "/api/users/10080/role-change-preview", """{"newRoleId":91}""");
        Assert.Equal(HttpStatusCode.OK, sameRole.StatusCode);
        using var doc = JsonDocument.Parse(text2);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Empty(doc.RootElement.GetProperty("adjustmentsToClear").EnumerateArray());
    }

    [Fact]
    public async Task Put_without_confirmation_gives_409_with_the_preview_body_and_writes_nothing()
    {
        var before = _f.Snapshot();
        var (resp, text) = await Send("admin", HttpMethod.Put, "/api/users/10080", Put(10080, 40, "adjusted1"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("adjusted1", doc.RootElement.GetProperty("accountLogin").GetString());
        Assert.Equal(before, _f.Snapshot());
        Assert.Equal(91, _f.Store.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 10080).IDROLE);
    }

    [Fact]
    public async Task Put_with_confirmation_changes_the_role_clears_the_adjustments_and_invalidates_the_cache()
    {
        using (var scope = _f.Services.CreateScope())
        {
            var rights = await scope.ServiceProvider.GetRequiredService<IEffectiveRightsService>().GetAsync(10081);   // met en cache
            Assert.True(rights.Has("PROC:SHIFTS"));
        }
        var otherRows = Rows(10083);
        Assert.True(otherRows > 0);

        var (resp, _) = await Send("admin", HttpMethod.Put, "/api/users/10081", Put(10081, 40, "adjusted2", confirm: true));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(40, _f.Store.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 10081).IDROLE);
        Assert.Equal(0, Rows(10081));
        Assert.Equal(otherRows, Rows(10083));                                       // un autre compte n'est pas touché
        Assert.Contains(_f.Store.GetRepo<T_AUDIT_DROIT>().Items, a => a.ID_CIBLE == 10081 && a.ACTION == "RESET_USER_PERMS");
        using var after = _f.Services.CreateScope();
        var fresh = await after.ServiceProvider.GetRequiredService<IEffectiveRightsService>().GetAsync(10081);
        Assert.False(fresh.Has("PROC:SHIFTS"));                                     // le cache a été invalidé
    }

    [Fact]
    public async Task Put_changing_the_role_of_an_account_without_adjustments_needs_no_confirmation()
    {
        var (resp, _) = await Send("admin", HttpMethod.Put, "/api/users/10050", Put(10050, 40, "cible"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(40, _f.Store.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 10050).IDROLE);
    }

    [Fact]
    public async Task Put_keeping_the_same_role_keeps_the_adjustments_without_confirmation()
    {
        var rows = Rows(10083);
        var (resp, _) = await Send("admin", HttpMethod.Put, "/api/users/10083", Put(10083, 91, "adjusted3"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(rows, Rows(10083));
    }

    [Theory]
    [InlineData("param2")]
    [InlineData("joel")]
    public async Task Put_is_still_refused_to_non_administrators(string profile)
    {
        var before = _f.Snapshot();
        var (resp, _) = await Send(profile, HttpMethod.Put, "/api/users/10080", Put(10080, 40, "adjusted1", confirm: true));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(before, _f.Snapshot());
    }
}
