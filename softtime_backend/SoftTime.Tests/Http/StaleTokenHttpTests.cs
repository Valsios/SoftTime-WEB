using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Lot 5 : un jeton encore valide (30 min) ne prolonge plus des droits retirés. Le rôle et les portes sont relus en base
/// à chaque requête (cache de 30 s vidé à chaque modification de droits).
/// </summary>
public class StaleTokenHttpTests : IClassFixture<ApiFactory>
{
    private const decimal WorkerId = 10200;
    private const decimal DemotedAdminId = 10201;
    private const decimal DeletedId = 10202;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public StaleTokenHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (_f.Store)
        {
            var users = _f.Store.Get<Responsable>().Items;
            if (users.Any(u => u.IdResponsable == WorkerId)) return;
            users.Add(new Responsable { IdResponsable = WorkerId, IdRole = 91, Login = "worker", NomResponsable = "Worker" });
            users.Add(new Responsable { IdResponsable = DemotedAdminId, IdRole = 1, Login = "exadmin", NomResponsable = "Ex admin" });
            _f.Store.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = WorkerId, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        }
    }

    private Task<HttpResponseMessage> Send(string token, HttpMethod method, string url, string? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return _http.SendAsync(req);
    }

    private void Invalidate(decimal userId)
    {
        using var scope = _f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IEffectiveRightsService>().Invalidate(userId);
    }

    [Fact]
    public async Task GateRemovedByAdministrator_SameTokenIsRefusedImmediately()
    {
        // Arrange
        var token = ApiFactory.Token(WorkerId, 91, 3);
        (await Send(token, HttpMethod.Get, "/api/v1/shifts")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        // L'administrateur retire tous les droits du compte par l'écran Privilèges (le cache est vidé).
        var save = await Send(ApiFactory.TokenFor("admin")!, HttpMethod.Put, $"/api/v1/privileges/user/{WorkerId}", """{"grantedPermissions":[]}""");
        var after = await Send(token, HttpMethod.Get, "/api/v1/shifts");

        // Assert
        save.StatusCode.Should().Be(HttpStatusCode.NoContent);
        after.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DemotedAdministrator_OldAdminTokenLosesAdministratorRights()
    {
        // Arrange
        var token = ApiFactory.Token(DemotedAdminId, 1, 1, 2, 3, 4, 5, 6);
        (await Send(token, HttpMethod.Get, "/api/v1/audit/types")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        _f.Store.Get<Responsable>().Items.Single(u => u.IdResponsable == DemotedAdminId).IdRole = 91;
        Invalidate(DemotedAdminId);
        var before = _f.Snapshot();
        var adminWrite = await Send(token, HttpMethod.Post, "/api/v1/roles", """{"id":0,"nom":"Pirate"}""");
        var gatedRead = await Send(token, HttpMethod.Get, "/api/v1/audit/types");

        // Assert
        adminWrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gatedRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _f.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task DeletedAccount_ValidTokenIsRefusedButNotLoggedOut()
    {
        // Act
        // Compte absent de la base, jeton encore valide : 403 (pas 401, qui déclencherait un rafraîchissement).
        var resp = await Send(ApiFactory.Token(DeletedId, 1, 1, 2, 3, 4, 5, 6), HttpMethod.Get, "/api/v1/users");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExistingAdministrator_KeepsEveryRight()
    {
        // Act
        var resp = await Send(ApiFactory.TokenFor("admin")!, HttpMethod.Get, "/api/v1/users");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
