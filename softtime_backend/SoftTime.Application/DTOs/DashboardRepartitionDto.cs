namespace SoftTime.Application.DTOs;

public record DashboardRepartitionDto(
    IReadOnlyList<DashboardSliceDto> ParCategorie,
    IReadOnlyList<DashboardSliceDto> ParDepartement)
{
    public DashboardRepartitionDto() : this(Array.Empty<DashboardSliceDto>(), Array.Empty<DashboardSliceDto>()) { }
}
