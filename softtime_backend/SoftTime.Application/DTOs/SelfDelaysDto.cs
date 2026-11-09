namespace SoftTime.Application.DTOs;

/// <summary>Retards du salarié connecté sur la période.</summary>
public sealed record SelfDelaysDto(SelfPeriodDto Periode, IReadOnlyList<SelfDelayDto> Retards);
