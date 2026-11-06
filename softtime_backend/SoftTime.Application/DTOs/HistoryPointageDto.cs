namespace SoftTime.Application.DTOs;

// --- Archives des suppressions (ex-tables « Historique* » du desktop) ---

public record HistoryPointageDto(decimal Id, string? Matricule, DateTime? Date, TimeSpan? Heure, string? Type, DateTime SupprimeLe)
{
    public HistoryPointageDto() : this(default, default, default, default, default, default) { }
}
