using System.Net;
using System.Text;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Complète ApiHttpSecurityTests (refus pour anonyme / sans porte / porte 2 et succès administrateur) avec le profil manquant :
/// un compte NON administrateur qui a la porte Bases et des permissions fines par base. Il ne peut que ce que ses permissions disent.
/// </summary>
public class DatabaseConfigurationGateHttpTests : IClassFixture<ApiFactory>
{
    private const decimal Gestionnaire = 10620;
    private const decimal RoleGestion = 96;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public DatabaseConfigurationGateHttpTests(ApiFactory factory)
    {
        _f = factory;
        _f.ResetStore();
        _http = factory.CreateClient();
        lock (_f.Store)
        {
            _f.Store.Get<Role>().Items.Add(new Role { IdRole = RoleGestion, NomRole = "GestionBases" });
            _f.Store.Get<DroitRole>().Items.Add(new DroitRole { IdRole = RoleGestion, IdDroit = 1 });
            _f.Store.Get<Responsable>().Items.Add(new Responsable { IdResponsable = Gestionnaire, IdRole = RoleGestion, Login = "gestion-bases" });
            foreach (var code in new[] { "SAGE:MODIF_SAGE:READ", "SAGE:MODIF_SAGE:UPDATE" })
                _f.Store.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = Gestionnaire, PermissionCode = code, IsGranted = true });
        }
    }

    private async Task<HttpStatusCode> SendAsync(HttpMethod method, string url, string? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiFactory.Token(Gestionnaire, RoleGestion, 1)}");
        return (await _http.SendAsync(req)).StatusCode;
    }

    [Fact]
    public async Task Gate1WithFinePermission_UpdatesItsDatabaseButNothingElse()
    {
        // Act
        var modifier = await SendAsync(HttpMethod.Put, "/api/v1/sage-databases/3036",
            """{"id":3036,"serveur":"invalid.host.test","sqlAuth":true,"nomBd":"MODIF_SAGE","typeBase":"STANDARD"}""");
        var supprimerLaSienne = await SendAsync(HttpMethod.Delete, "/api/v1/sage-databases/3036");
        var supprimerUneAutre = await SendAsync(HttpMethod.Delete, "/api/v1/sage-databases/3035");
        var creer = await SendAsync(HttpMethod.Post, "/api/v1/sage-databases",
            """{"id":0,"serveur":"h","sqlAuth":true,"nomBd":"NEWBASE","typeBase":"STANDARD"}""");
        var roles = await SendAsync(HttpMethod.Post, "/api/v1/roles", """{"id":0,"nom":"Interdit"}""");

        // Assert
        modifier.Should().Be(HttpStatusCode.OK);
        supprimerLaSienne.Should().Be(HttpStatusCode.Forbidden);     // UPDATE sans DELETE
        supprimerUneAutre.Should().Be(HttpStatusCode.Forbidden);
        creer.Should().Be(HttpStatusCode.Forbidden);                // pas de SAGE:CREATE
        roles.Should().Be(HttpStatusCode.Forbidden);                // réservé à l'administrateur
        _f.Store.Get<BddSage>().Items.Should().Contain(s => s.Id == 3035).And.Contain(s => s.Id == 3036);
    }
}
