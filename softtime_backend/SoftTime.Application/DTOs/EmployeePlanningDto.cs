namespace SoftTime.Application.DTOs;

public record EmployeePlanningDto(int Id, int CardPaieId, int? NoShift, DateTime? DateP, DateTime? Ha, TimeSpan? Pause, DateTime? Hd, bool? Off, string? Matricule = null, string? Nom = null, string? Prenom = null)
{
    public EmployeePlanningDto() : this(default, default, default, default, default, default, default, default) { }
}
