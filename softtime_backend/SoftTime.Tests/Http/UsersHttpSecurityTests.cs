using System.Net;

using Microsoft.Extensions.DependencyInjection;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Étape 4b-1 : créer, modifier et supprimer un compte (et changer son rôle ou son mot de passe) est réservé aux administrateurs.
/// Hôte en mémoire, jetons de test, aucun mot de passe réel.
/// </summary>
public class UsersHttpSecurityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public UsersHttpSecurityTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    // Valeur factice conforme à la politique de mot de passe ; compte jetable du jeu de test.
    private const string NewUser = """{"id":0,"roleId":91,"nom":"Nouveau","login":"nouveau","password":"ValidP@ss123!"}""";
    private const string Promote = """{"id":10052,"roleId":40,"nom":"Modifiable","login":"modifiable","password":"ValidP@ss123!"}""";

    private static readonly Dictionary<string, (string Method, string Url, string? Body)> Routes = new()
    {
        ["POST users"] = ("POST", "/api/v1/users", NewUser),
        ["PUT users/{id} (rôle + mot de passe)"] = ("PUT", "/api/v1/users/10052", Promote),
        ["DELETE users/{id}"] = ("DELETE", "/api/v1/users/10051", null),
    };

    public static IEnumerable<object[]> RefusedCases()
        => Routes.Keys.SelectMany(r => new[] { "anonyme", "param2", "joel", "user1" }.Select(p => new object[] { r, p }));

    public static IEnumerable<object[]> Routes_() => Routes.Keys.Select(r => new object[] { r });

    private async Task<(HttpResponseMessage Resp, string Body)> Send(string profile, string method, string url, string? body)
    {
        var resp = await _http.SendAsync(_f.Request(profile, new HttpMethod(method), url, body));
        return (resp, await resp.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public async Task Non_administrators_cannot_write_other_accounts_and_nothing_is_written(string route, string profile)
    {
        var (method, url, body) = Routes[route];
        var before = _f.Snapshot();
        var (resp, text) = await Send(profile, method, url, body);
        ApiFactory.Report($"4B1|{route}|{profile}|{(int)resp.StatusCode}|{(text.Length == 0 ? "corps vide" : text)}");

        Assert.Equal(profile == "anonyme" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(before, _f.Snapshot());
        // param2 a la porte 2 : il atteint le contrôle administrateur et reçoit le message prévu.
        if (profile == "param2") Assert.Contains(AdminAccess.ReservedMessage, text);
    }

    [Fact]
    public async Task Administrator_can_create_modify_change_role_and_password_and_delete()
    {
        var (created, _) = await Send("admin", "POST", "/api/v1/users", NewUser);
        Assert.True(created.IsSuccessStatusCode, created.StatusCode.ToString());
        Assert.Contains(_f.Store.GetRepo<Responsable>().Items, u => u.Login == "nouveau");

        var (updated, _) = await Send("admin", "PUT", "/api/v1/users/10052", Promote);
        Assert.True(updated.IsSuccessStatusCode, updated.StatusCode.ToString());
        Assert.Equal(40, _f.Store.GetRepo<Responsable>().Items.Single(u => u.IdResponsable == 10052).IdRole);

        var (deleted, _) = await Send("admin", "DELETE", "/api/v1/users/10051", null);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain(_f.Store.GetRepo<Responsable>().Items, u => u.IdResponsable == 10051);
    }

    [Fact]
    public void Production_container_provides_ICurrentUser_to_AuthService()
    {
        // ICurrentUser est obligatoire dans AuthService : si l'enregistrement de production disparaît, ce test échoue.
        using var scope = _f.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SoftTime.Application.Abstractions.ICurrentUser>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserAdministrationService>());
    }

    [Fact]
    public async Task Reading_users_needs_the_fine_permission_since_step_4b_9()
    {
        // Avant 4b-9 : la porte 2 seule suffisait (param2 lisait les comptes en 200). Maintenant : porte 2 ET PARAMS:USERS.
        foreach (var url in new[] { "/api/v1/users", "/api/v1/users/10052" })
        {
            var (ok, _) = await Send("usersreader", "GET", url, null);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var (refused, text) = await Send("param2", "GET", url, null);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
        // Sans la porte 2, la lecture reste refusée comme avant.
        var (denied, _) = await Send("joel", "GET", "/api/v1/users", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
