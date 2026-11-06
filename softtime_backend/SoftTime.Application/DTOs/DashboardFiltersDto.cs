namespace SoftTime.Application.DTOs;

/// <summary>Valeurs disponibles pour les filtres du tableau de bord analytique.</summary>
public record DashboardFiltersDto(
    IReadOnlyList<string> Departements,
    IReadOnlyList<DashboardOptionDto> Categories)
{
    public DashboardFiltersDto() : this(Array.Empty<string>(), Array.Empty<DashboardOptionDto>()) { }
}
