using System.Net;

namespace SoftTime.Tests.Http;

/// <summary>Protection du dernier administrateur, de bout en bout : le jeu de test n'a qu'un administrateur (14).</summary>
public class LastAdministratorHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public LastAdministratorHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task DemoteLastAdministrator_IsRefusedAndChangesNothing()
    {
        // Arrange
        // Seul un administrateur modifie un administrateur, et personne ne change son propre rôle : le dernier
        // administrateur ne peut donc être rétrogradé par personne (la règle « dernier administrateur » du service
        // couvre en plus les appels internes, voir AdminRulesAndSafeguardsTests).
        var before = _f.Snapshot();

        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Put, "/api/v1/users/14",
            """{"id":14,"roleId":91,"nom":"Admin","login":"admin","confirmClearAdjustments":true}"""));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Vous ne pouvez pas modifier votre propre rôle.");
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task DeleteLastAdministrator_IsRefusedAndChangesNothing()
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Delete, "/api/v1/users/14"));

        // Assert
        resp.IsSuccessStatusCode.Should().BeFalse();
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task DeleteAdministratorRole_IsRefused()
    {
        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Delete, "/api/v1/roles/1"));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Le rôle Administrateur ne peut pas être supprimé.");
    }
}
