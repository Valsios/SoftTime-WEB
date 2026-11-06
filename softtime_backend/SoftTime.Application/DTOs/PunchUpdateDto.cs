namespace SoftTime.Application.DTOs;

// --- Corrections et purges de la période de traitement ---

/// <summary>Correction d'un pointage importé : les champs null restent inchangés.</summary>
public record PunchUpdateDto(DateTime? Date, TimeSpan? Heure, string? Type)
{
    public PunchUpdateDto() : this(default, default, default) { }
}
