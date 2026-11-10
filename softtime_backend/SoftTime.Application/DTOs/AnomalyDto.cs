namespace SoftTime.Application.DTOs;

public record AnomalyDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? H1, TimeSpan? H2, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, bool? Feries, bool? FeriesAm, bool? FeriesPm, string? IntituleAbsence)
{
    public AnomalyDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}
