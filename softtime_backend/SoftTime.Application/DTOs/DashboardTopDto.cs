namespace SoftTime.Application.DTOs;

public record DashboardTopDto(string Matricule, string? Nom, string? Prenom, string? Departement, int Count, int TotalMinutes)
{
    public DashboardTopDto() : this(string.Empty, default, default, default, default, default) { }
}
