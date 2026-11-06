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
    public async Task Deleting_an_account_over_http_removes_its_rights_and_database_access_but_not_others()
    {
        var joelRows = _f.Store.GetRepo<T_USER_PERMISSION>().Items.Count(p => p.IDRESPONSABLE == 10034);
        Assert.Contains(_f.Store.GetRepo<T_USER_PERMISSION>().Items, p => p.IDRESPONSABLE == 10053);
        Assert.Contains(_f.Store.GetRepo<T_BDD_AUTORISE>().Items, a => a.IDRESPONSABLE == 10053);

        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Delete, "/api/users/10053"));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.DoesNotContain(_f.Store.GetRepo<T_RESPONSABLE>().Items, u => u.IDRESPONSABLE == 10053);
        Assert.DoesNotContain(_f.Store.GetRepo<T_USER_PERMISSION>().Items, p => p.IDRESPONSABLE == 10053);
        Assert.DoesNotContain(_f.Store.GetRepo<T_BDD_AUTORISE>().Items, a => a.IDRESPONSABLE == 10053);
        Assert.Equal(joelRows, _f.Store.GetRepo<T_USER_PERMISSION>().Items.Count(p => p.IDRESPONSABLE == 10034));
        Assert.Contains(_f.Store.GetRepo<T_BDD_AUTORISE>().Items, a => a.IDRESPONSABLE == 14);
    }

    [Fact]
    public async Task A_refused_deletion_over_http_removes_no_row()
    {
        var before = _f.Snapshot();
        // L'administrateur ne peut pas supprimer son propre compte (14).
        var resp = await _http.SendAsync(_f.Request("admin", HttpMethod.Delete, "/api/users/14"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(before, _f.Snapshot());
    }
}
