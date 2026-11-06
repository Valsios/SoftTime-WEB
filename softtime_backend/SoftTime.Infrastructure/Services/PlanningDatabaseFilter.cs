using SoftTime.Application.Abstractions;

namespace SoftTime.Infrastructure.Services;

public sealed class PlanningDatabaseFilter : IPlanningDatabaseFilter
{
    private readonly ITenantConnectionService _tenant;

    public PlanningDatabaseFilter(
        ITenantConnectionService tenant)
    {
        _tenant = tenant;
    }

    public bool MatchesSage(string? stored)
    {
        var sage = _tenant.SageDb;
        if (string.IsNullOrWhiteSpace(stored))
            return true;
        return string.Equals(stored.Trim(), sage.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesPointeuse(string? stored)
    {
        var pointeuse = _tenant.PointeuseDb;
        if (string.IsNullOrWhiteSpace(stored))
            return true;
        return string.Equals(stored.Trim(), pointeuse.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
