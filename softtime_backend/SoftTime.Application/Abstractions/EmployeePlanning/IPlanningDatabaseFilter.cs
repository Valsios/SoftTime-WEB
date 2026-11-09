namespace SoftTime.Application.Abstractions.EmployeePlanning;

public interface IPlanningDatabaseFilter
{
    bool MatchesSage(string? stored);

    bool MatchesPointeuse(string? stored);
}
