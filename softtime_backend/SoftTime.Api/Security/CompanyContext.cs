using SoftTime.Application.Abstractions.RequestContext;

namespace SoftTime.Api.Security;

public class CompanyContext : ICompanyContext
{
    public string? SageDatabase { get; private set; }
    public string? PointeuseDatabase { get; private set; }
    public void Set(string sage, string? pointeuse)
    {
        SageDatabase = sage;
        if (!string.IsNullOrWhiteSpace(pointeuse))
            PointeuseDatabase = pointeuse;
    }
}
