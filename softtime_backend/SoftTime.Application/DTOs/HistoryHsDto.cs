namespace SoftTime.Application.DTOs;

public record HistoryHsDto(decimal Id, string? Matricule, DateTime? PeriodeDebut, DateTime? PeriodeFin, decimal? Exo130, decimal? Exo150, decimal? I130, decimal? I150, DateTime SupprimeLe)
{
    public HistoryHsDto() : this(default, default, default, default, default, default, default, default, default) { }
}
