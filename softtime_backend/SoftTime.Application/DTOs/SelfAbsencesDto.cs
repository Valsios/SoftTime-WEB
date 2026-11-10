namespace SoftTime.Application.DTOs;

/// <summary>Absences et congés du salarié connecté sur la période.</summary>
public sealed record SelfAbsencesDto(SelfPeriodDto Periode, IReadOnlyList<SelfAbsenceDto> Absences);
