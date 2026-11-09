using System.Net;

using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Écritures de l'écran Privilèges, de bout en bout (jeton, filtres, contrôleur, service réel, magasin en mémoire).
/// </summary>
public class PrivilegesWriteHttpTests : IClassFixture<ApiFactory>
{
    private const decimal EmployeeRoleId = 90;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public PrivilegesWriteHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task SaveRolePermissions_EmployeeRoleWithSelfRead_IsSaved()
    {
        // Arrange
        var body = $$"""{"grantedPermissions":["{{PermissionCatalog.SelfRead}}"]}""";

        // Act
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Put, $"/api/v1/privileges/role/{EmployeeRoleId}", body));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, await resp.Content.ReadAsStringAsync());
        _f.Store.Get<RolePermission>().Items
            .Where(p => p.IdRole == EmployeeRoleId)
            .Select(p => p.PermissionCode)
            .Should().Equal(PermissionCatalog.SelfRead);
    }
}
