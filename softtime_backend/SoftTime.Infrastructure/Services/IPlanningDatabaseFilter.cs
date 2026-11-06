namespace SoftTime.Infrastructure.Services;

public interface IPlanningDatabaseFilter
{
    bool MatchesSage(string? stored);

    bool MatchesPointeuse(string? stored);
}
