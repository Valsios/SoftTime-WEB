namespace SoftTime.Application.DTOs;

/// <summary>Détail du calcul des heures supplémentaires d'un salarié (remplace le <c>DetailHS</c> du desktop).</summary>
public record OvertimeDetailDto(
    string? Matricule,
    DateTime From,
    DateTime To,
    IReadOnlyList<OvertimeDetailDayDto> Jours,
    IReadOnlyList<OvertimeDetailWeekDto> Semaines,
    HsExoDto? ExoImpo)
{
    public OvertimeDetailDto() : this(default, default, default, new List<OvertimeDetailDayDto>(), new List<OvertimeDetailWeekDto>(), default) { }
}
