namespace SoftTime.Application.DTOs;

/// <summary>Classements d'assiduité : les plus ponctuels et les moins ponctuels de la période.</summary>
public record DashboardAssiduiteDto(
    IReadOnlyList<DashboardAssiduiteRowDto> PlusPonctuels,
    IReadOnlyList<DashboardAssiduiteRowDto> MoinsPonctuels)
{
    public DashboardAssiduiteDto() : this(Array.Empty<DashboardAssiduiteRowDto>(), Array.Empty<DashboardAssiduiteRowDto>()) { }
}
