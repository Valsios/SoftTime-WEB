namespace SoftTime.Application.DTOs;

/// <summary>Semaines validées et cumuls EXO / IMPO du salarié connecté sur la période.</summary>
public sealed record SelfWeeksDto(
    SelfPeriodDto Periode,
    IReadOnlyList<SelfWeekDto> Semaines,
    IReadOnlyList<SelfExoImpoDto> ExoImpo);
