using System.Net;

using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Lot 7 : suppression et purge de l'Historique réservées à l'administrateur (rôle relu en base) ; lectures sous
/// TRACE:AUDIT.
/// </summary>
public class HistoryAdminHttpTests : IClassFixture<ApiFactory>
{
    private const decimal TraceReaderId = 10300;
    private const decimal LegacyTraceId = 10301;
    private const decimal LegacyTraceRoleId = 9305;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public HistoryAdminHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (_f.Store)
        {
            var users = _f.Store.Get<Responsable>().Items;
            if (users.Any(u => u.IdResponsable == TraceReaderId)) return;
            // L'Historique lit la base SAGE active : chaque compte a la base cochée et le droit de la lire (porte 1).
            users.Add(new Responsable { IdResponsable = TraceReaderId, IdRole = 91, Login = "historeader" });
            foreach (var code in new[] { "TRACE:AUDIT", $"SAGE:{ApiFactory.SageName}:READ" })
                _f.Store.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = TraceReaderId, PermissionCode = code, IsGranted = true });
            // Compte hérité : rôle avec les portes 1 et 5, aucune permission fine.
            _f.Store.Get<Role>().Items.Add(new Role { IdRole = LegacyTraceRoleId, NomRole = "Traçabilité héritée" });
            foreach (var gate in new[] { 1, 5 })
                _f.Store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 9300 + gate, IdRole = LegacyTraceRoleId, IdDroit = gate });
            users.Add(new Responsable { IdResponsable = LegacyTraceId, IdRole = LegacyTraceRoleId, Login = "histolegacy" });
            foreach (var id in new[] { TraceReaderId, LegacyTraceId })
                _f.Store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = (int)id, IdResponsable = id, IdBdd = 3033 });
        }
    }

    public static TheoryData<string> Deletes() => new()
    {
        "/api/v1/history/pointages/1", "/api/v1/history/pointages",
        "/api/v1/history/anomalies/1", "/api/v1/history/anomalies",
        "/api/v1/history/hs/1", "/api/v1/history/hs",
    };

    private Task<HttpResponseMessage> Send(string? token, HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (token is not null)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
        return _http.SendAsync(req);
    }

    [Theory]
    [MemberData(nameof(Deletes))]
    public async Task HistoryDelete_Anonymous_Returns401(string url)
    {
        // Act
        var resp = await Send(null, HttpMethod.Delete, url);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Deletes))]
    public async Task HistoryDelete_NonAdministratorWithTraceAudit_Returns403AndDeletesNothing(string url)
    {
        foreach (var (id, role) in new[] { (TraceReaderId, 91m), (LegacyTraceId, LegacyTraceRoleId) })
        {
            // Arrange
            var before = _f.Snapshot();

            // Act
            var resp = await Send(ApiFactory.Token(id, role, 1, 5), HttpMethod.Delete, url);

            // Assert
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"compte {id}");
            (await resp.Content.ReadAsStringAsync()).Should().Contain(AdminAccess.ReservedMessage);
            _f.Snapshot().Should().Be(before);
        }
    }

    [Theory]
    [MemberData(nameof(Deletes))]
    public async Task HistoryDelete_Administrator_Succeeds(string url)
    {
        // Act
        var resp = await Send(ApiFactory.TokenFor("admin"), HttpMethod.Delete, url);

        // Assert
        // Ligne unique absente du magasin de test : 404 ; purge : 200.
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(10300, 91)]
    [InlineData(10301, 9305)]
    public async Task HistoryRead_TraceAuditOrLegacyGate_StaysOpen(int id, int role)
    {
        // Act
        var resp = await Send(ApiFactory.Token(id, role, 1, 5), HttpMethod.Get, "/api/v1/history/hs");

        // Assert
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
