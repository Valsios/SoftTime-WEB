using System.Net;
using System.Text;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Lot 4 : permissions fines vérifiées côté serveur sur les écritures, de bout en bout (jeton, filtres, droits effectifs
/// calculés en base). Pour chaque famille : porte sans la permission fine → 403 ; avec la permission → OK ;
/// administrateur → OK ; compte hérité sans aucune permission fine enregistrée → OK (droits par défaut de ses portes).
/// </summary>
public class FinePermissionsHttpTests : IClassFixture<ApiFactory>
{
    private const decimal LegacyRoleId = 95;

    /// <summary>Comptes de test : identifiant, rôle, portes du jeton, permissions enregistrées sur le compte.</summary>
    private static readonly (decimal Id, decimal Role, int[] Gates, string[] Codes)[] Accounts =
    {
        (10100, 91, new[] { 3 }, new[] { "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE" }),
        (10101, 91, new[] { 3 }, new[] { "PROC:SHIFTS", "PROC:SHIFTS:DELETE" }),
        (10102, LegacyRoleId, new[] { 2, 3 }, Array.Empty<string>()),
    };

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public FinePermissionsHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        Seed();
    }

    private void Seed()
    {
        lock (_f.Store)
        {
            if (_f.Store.Get<Role>().Items.Any(r => r.IdRole == LegacyRoleId)) return;
            // Rôle hérité : portes 2 et 3, aucune ligne de permission fine (ni sur le rôle ni sur ses comptes).
            _f.Store.Get<Role>().Items.Add(new Role { IdRole = LegacyRoleId, NomRole = "Hérité" });
            _f.Store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 9501, IdRole = LegacyRoleId, IdDroit = 2 });
            _f.Store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 9502, IdRole = LegacyRoleId, IdDroit = 3 });
            foreach (var (id, role, _, codes) in Accounts)
            {
                _f.Store.Get<Responsable>().Items.Add(new Responsable { IdResponsable = id, IdRole = role, Login = $"fine{id}" });
                foreach (var code in codes)
                    _f.Store.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = id, PermissionCode = code, IsGranted = true });
            }
        }
    }

    private Task<HttpResponseMessage> Send(decimal userId, HttpMethod method, string url, string? body = null)
    {
        var (_, role, gates, _) = userId == 14 ? (14m, 1m, new[] { 1, 2, 3, 4, 5, 6 }, Array.Empty<string>()) : Accounts.Single(a => a.Id == userId);
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiFactory.Token(userId, role, gates)}");
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return _http.SendAsync(req);
    }

    private void AddShift() => _f.Store.Get<Planning>().Items.Add(new Planning { IdPlanning = 1, NoShift = 1 });

    [Fact]
    public async Task DeleteShift_GateWithoutFinePermission_Returns403AndKeepsShift()
    {
        // Arrange
        AddShift();
        var before = _f.Snapshot();

        // Act
        var resp = await Send(10100, HttpMethod.Delete, "/api/v1/shifts/1");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Vous n'avez pas la permission requise pour cette action.");
        _f.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData(10101)]
    [InlineData(14)]
    [InlineData(10102)]
    public async Task DeleteShift_FinePermissionAdministratorOrLegacyAccount_Succeeds(int userId)
    {
        // Arrange
        AddShift();

        // Act
        var resp = await Send(userId, HttpMethod.Delete, "/api/v1/shifts/1");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, await resp.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("PUT", "/api/v1/shifts/1")]
    [InlineData("DELETE", "/api/v1/employee-planning/1")]
    [InlineData("POST", "/api/v1/corrected-hours")]
    [InlineData("DELETE", "/api/v1/anomalies/1")]
    [InlineData("POST", "/api/v1/anomalies/validate")]
    [InlineData("PUT", "/api/v1/punches/1")]
    [InlineData("POST", "/api/v1/weekly-validation/validate")]
    [InlineData("POST", "/api/v1/overtime/calculate")]
    public async Task ProcessingWrites_GateWithoutFinePermission_Return403(string method, string url)
    {
        // Act
        // Le compte 10101 a la porte Traitements et seulement les droits des shifts (lecture et suppression).
        var resp = await Send(10101, new HttpMethod(method), url, "{}");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProcessingReads_GateWithoutScreenPermission_StayOpen()
    {
        // Act
        // Les lectures (listes déroulantes partagées entre écrans) restent protégées par la porte seule.
        var resp = await Send(10101, HttpMethod.Get, "/api/v1/shifts");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
