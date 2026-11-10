namespace SoftTime.Application.DTOs;

public record WeeklyHsDto(int Id, string? Matricule, DateTime? DateDeb, DateTime? DateFin, decimal? Ht, decimal? Hs, decimal? Nuit, decimal? Dimanche, decimal? Feries, decimal? Retard, decimal? Absence, bool? Validated)
{
    public WeeklyHsDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}
