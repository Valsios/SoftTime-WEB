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

    private int Rows(decimal id) => _f.Store.Get<UserPermission>().Items.Count(p => p.IdResponsable == id);

    [Fact]
    public async Task Preview_for_an_administrator_lists_the_adjustments_that_would_be_cleared()
    {
        var before = _f.Snapshot();
        var (resp, text) = await Send("admin", HttpMethod.Post, "/api/v1/users/10080/role-change-preview", """{"newRoleId":40}""");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("data").GetProperty("accountLogin").GetString().Should().Be("adjusted1");
        var codes = doc.RootElement.GetProperty("data").GetProperty("adjustmentsToClear").EnumerateArray().Select(e => e.GetString()).ToList();
        codes.OrderBy(c => c).Should().Equal(new[] { "+PROC:PUNCHES", "+PROC:SHIFTS" });
        _f.Snapshot().Should().Be(before);                 // l'aperçu n'écrit rien
    }

    [Theory]
    [InlineData("param2")]
    [InlineData("usersreader")]
    [InlineData("joel")]
    [InlineData("user1")]
    public async Task Preview_is_refused_to_non_administrators(string profile)
    {
        var (resp, _) = await Send(profile, HttpMethod.Post, "/api/v1/users/10080/role-change-preview", """{"newRoleId":40}""");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Preview_is_refused_to_anonymous()
    {
        var (resp, _) = await Send("anonyme", HttpMethod.Post, "/api/v1/users/10080/role-change-preview", """{"newRoleId":40}""");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Preview_has_count_zero_without_adjustments_or_when_the_role_does_not_change()
    {
        var (withoutAdjustments, text1) = await Send("admin", HttpMethod.Post, "/api/v1/users/10050/role-change-preview", """{"newRoleId":40}""");
        withoutAdjustments.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(text1).RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(0);

        var (sameRole, text2) = await Send("admin", HttpMethod.Post, "/api/v1/users/10080/role-change-preview", """{"newRoleId":91}""");
        sameRole.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(text2);
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("data").GetProperty("adjustmentsToClear").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Put_without_confirmation_gives_409_with_the_preview_body_and_writes_nothing()
    {
        var before = _f.Snapshot();
        var (resp, text) = await Send("admin", HttpMethod.Put, "/api/v1/users/10080", Put(10080, 40, "adjusted1"));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("data").GetProperty("accountLogin").GetString().Should().Be("adjusted1");
        _f.Snapshot().Should().Be(before);
        _f.Store.Get<Responsable>().Items.Single(u => u.IdResponsable == 10080).IdRole.Should().Be(91);
    }

    [Fact]
    public async Task Put_with_confirmation_changes_the_role_clears_the_adjustments_and_invalidates_the_cache()
    {
        using (var scope = _f.Services.CreateScope())
        {
            var rights = await scope.ServiceProvider.GetRequiredService<IEffectiveRightsService>().GetAsync(10081);   // met en cache
            rights.Has("PROC:SHIFTS").Should().BeTrue();
        }
        var otherRows = Rows(10083);
        (otherRows > 0).Should().BeTrue();

        var (resp, _) = await Send("admin", HttpMethod.Put, "/api/v1/users/10081", Put(10081, 40, "adjusted2", confirm: true));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        _f.Store.Get<Responsable>().Items.Single(u => u.IdResponsable == 10081).IdRole.Should().Be(40);
        Rows(10081).Should().Be(0);
        Rows(10083).Should().Be(otherRows);                                       // un autre compte n'est pas touché
        _f.Store.Get<AuditDroit>().Items.Should().Contain(a => a.IdCible == 10081 && a.Action == "RESET_USER_PERMS");
        using var after = _f.Services.CreateScope();
        var fresh = await after.ServiceProvider.GetRequiredService<IEffectiveRightsService>().GetAsync(10081);
        fresh.Has("PROC:SHIFTS").Should().BeFalse();                                     // le cache a été invalidé
    }

    [Fact]
    public async Task Put_changing_the_role_of_an_account_without_adjustments_needs_no_confirmation()
    {
        var (resp, _) = await Send("admin", HttpMethod.Put, "/api/v1/users/10050", Put(10050, 40, "cible"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        _f.Store.Get<Responsable>().Items.Single(u => u.IdResponsable == 10050).IdRole.Should().Be(40);
    }

    [Fact]
    public async Task Put_keeping_the_same_role_keeps_the_adjustments_without_confirmation()
    {
        var rows = Rows(10083);
        var (resp, _) = await Send("admin", HttpMethod.Put, "/api/v1/users/10083", Put(10083, 91, "adjusted3"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Rows(10083).Should().Be(rows);
    }

    [Theory]
    [InlineData("param2")]
    [InlineData("joel")]
    public async Task Put_is_still_refused_to_non_administrators(string profile)
    {
        var before = _f.Snapshot();
        var (resp, _) = await Send(profile, HttpMethod.Put, "/api/v1/users/10080", Put(10080, 40, "adjusted1", confirm: true));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _f.Snapshot().Should().Be(before);
    }
}
