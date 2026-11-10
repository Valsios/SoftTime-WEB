using System.Net;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

public class AccountDeletionHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public AccountDeletionHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task DeleteUser_ExistingAccount_RemovesItsRightsAndDatabaseAccessOnly()
    {
        // Act
        var joelRows = _f.Store.Get<UserPermission>().Items.Count(p => p.IdResponsable == 10034);

        // Assert
        _f.Store.Get<UserPermission>().Items.Should().Contain(p => p.IdResponsable == 10053);
        _f.Store.Get<BddAutorisee>().Items.Should().Contain(a => a.IdResponsable == 10053);
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Delete, "/api/v1/users/10053"));
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _f.Store.Get<Responsable>().Items.Should().NotContain(u => u.IdResponsable == 10053);
        _f.Store.Get<UserPermission>().Items.Should().NotContain(p => p.IdResponsable == 10053);
        _f.Store.Get<BddAutorisee>().Items.Should().NotContain(a => a.IdResponsable == 10053);
        _f.Store.Get<UserPermission>().Items.Count(p => p.IdResponsable == 10034).Should().Be(joelRows);
        _f.Store.Get<BddAutorisee>().Items.Should().Contain(a => a.IdResponsable == 14);
    }

    [Fact]
    public async Task DeleteUser_RefusedDeletion_RemovesNoRow()
    {
        // Arrange
        var before = _f.Snapshot();

        // Act
        // L'administrateur ne peut pas supprimer son propre compte (14).
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Delete, "/api/v1/users/14"));

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _f.Snapshot().Should().Be(before);
    }
}
