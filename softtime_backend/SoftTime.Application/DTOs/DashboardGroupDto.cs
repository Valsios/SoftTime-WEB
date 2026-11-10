namespace SoftTime.Application.DTOs;

public record DashboardGroupDto(string Periode, int Count, int TotalMinutes)
{
    public DashboardGroupDto() : this(string.Empty, default, default) { }
}
