namespace SoftTime.Application.DTOs;

public record DashboardRetardsDto(int Total, int TotalMinutes, IReadOnlyList<DashboardGroupDto> Stats, IReadOnlyList<DashboardTopDto> Top)
{
    public DashboardRetardsDto() : this(default, default, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()) { }
}
