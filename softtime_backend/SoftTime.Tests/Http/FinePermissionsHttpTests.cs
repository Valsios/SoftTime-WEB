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
        (10102, LegacyRoleId, new[] { 2, 3, 4, 5, 6 }, Array.Empty<string>()),
        (10103, 91, new[] { 2 }, new[] { "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:UPDATE", "PARAMS:CATEGORIES" }),
        (10104, 91, new[] { 2 }, new[] { "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:CREATE", "PARAMS:CARDPAIE" }),
        // Lot 4c : jeton avec les portes 4, 5 et 6 ; droits effectifs limités à la cantine et aux prévisions.
        (10105, 91, new[] { 4, 5, 6 }, new[] { "OTHER:CANTEEN", "REPORTS:PREDICTIONS" }),
        (10106, 91, new[] { 4, 5, 6 }, new[] { "OTHER:TRAVEL_EXPENSES", "TRACE:AUDIT", "REPORTS:REPORTS" }),
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
            // Rôle hérité : portes 2 à 6, aucune ligne de permission fine (ni sur le rôle ni sur ses comptes).
            _f.Store.Get<Role>().Items.Add(new Role { IdRole = LegacyRoleId, NomRole = "Hérité" });
            foreach (var gate in new[] { 2, 3, 4, 5, 6 })
                _f.Store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 9500 + gate, IdRole = LegacyRoleId, IdDroit = gate });
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

    private static string Holiday(int userId) => $$"""{"id":0,"intitule":"Férié {{userId}}","date":"2030-01-{{userId % 28 + 1:00}}T00:00:00"}""";

    [Fact]
    public async Task CreateHoliday_GateWithoutFinePermission_Returns403AndChangesNothing()
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var resp = await Send(10103, HttpMethod.Post, "/api/v1/holidays", Holiday(10103));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Vous n'avez pas la permission requise pour cette action.");
        _f.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData(10104)]
    [InlineData(14)]
    [InlineData(10102)]
    public async Task CreateHoliday_FinePermissionAdministratorOrLegacyAccount_Succeeds(int userId)
    {
        // Act
        var resp = await Send(userId, HttpMethod.Post, "/api/v1/holidays", Holiday(userId));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("PUT", "/api/v1/categories/1")]
    [InlineData("DELETE", "/api/v1/tolerances/1")]
    [InlineData("PUT", "/api/v1/majorations/1")]
    [InlineData("POST", "/api/v1/absence-codes/sync-sage")]
    [InlineData("POST", "/api/v1/card-paie/auto-map")]
    [InlineData("PUT", "/api/v1/clock-params")]
    [InlineData("PUT", "/api/v1/correspondence-mode")]
    [InlineData("PUT", "/api/v1/code-constantes/1")]
    [InlineData("POST", "/api/v1/departement-service/lookup-batch")]
    public async Task ParametersWrites_GateWithoutFinePermission_Return403(string method, string url)
    {
        // Act
        // Le compte 10103 a la porte Paramètres, et seulement les jours fériés (lecture, modification) et les catégories.
        var resp = await Send(10103, new HttpMethod(method), url, "{}");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST", "/api/v1/payroll-write-config")]
    [InlineData("DELETE", "/api/v1/payroll-write-config/1")]
    [InlineData("POST", "/api/v1/payroll-write-config/test")]
    public async Task PayrollWriteConfiguration_NonAdministratorWithParametersGate_Returns403(string method, string url)
    {
        // Act
        var resp = await Send(10102, new HttpMethod(method), url, "{}");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Cette opération est réservée aux administrateurs.");
    }

    [Fact]
    public async Task CardPaieRead_WithoutParametersGate_Returns403()
    {
        // Act
        var resp = await Send(10101, HttpMethod.Get, "/api/v1/card-paie");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Lot 4c : une route par écran ; tout l'écran (lectures et écritures) exige son code.</summary>
    public static TheoryData<string, string> ScreenRoutes() => new()
    {
        { "GET", "/api/v1/reports/pointage" },
        { "GET", "/api/v1/travel-expenses" },
        { "POST", "/api/v1/travel-expenses/compute" },
        { "GET", "/api/v1/history/pointages" },
        { "DELETE", "/api/v1/history/hs" },
        { "GET", "/api/v1/audit/types" },
        { "GET", "/api/v1/privileges/audit" },
    };

    [Theory]
    [MemberData(nameof(ScreenRoutes))]
    public async Task OtherTraceReportsScreens_GateWithoutScreenPermission_Return403(string method, string url)
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        var resp = await Send(10105, new HttpMethod(method), url, "{}");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _f.Snapshot().Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(ScreenRoutes))]
    public async Task OtherTraceReportsScreens_ScreenPermissionAdministratorOrLegacy_PassTheCheck(string method, string url)
    {
        foreach (var userId in new[] { 10106, 14, 10102 })
        {
            // Act
            // Le service peut ensuite échouer faute de base SAGE réelle : seul le contrôle des droits est vérifié ici.
            var resp = await Send(userId, new HttpMethod(method), url, "{}");

            // Assert
            resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"compte {userId} sur {method} {url}");
            resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        }
    }
}
