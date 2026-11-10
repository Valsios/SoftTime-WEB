namespace SoftTime.Application.DTOs;

public record HistoryAnomalieDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? HEntree, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, string? IntituleAbsence, DateTime SupprimeLe)
{
    public HistoryAnomalieDto() : this(default, default, default, default, default, default, default, default, default, default) { }
}
