using SoftTime.Application.Abstractions;

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

public class CurrentUser : ICurrentUser
{
    public decimal? UserId { get; init; }
    public string? Login { get; init; }
    public decimal? RoleId { get; init; }
    public string? Matricule { get; init; }
    public IReadOnlyCollection<int> Rights { get; init; } = Array.Empty<int>();
    public bool HasRight(int droit) => Rights.Contains(droit);
}
