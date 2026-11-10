namespace SoftTime.Application.DTOs;

public record DashboardOptionDto(int Id, string Libelle)
{
    public DashboardOptionDto() : this(default, string.Empty) { }
}
