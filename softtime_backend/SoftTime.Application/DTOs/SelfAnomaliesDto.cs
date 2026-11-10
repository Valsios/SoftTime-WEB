namespace SoftTime.Application.DTOs;

/// <summary>Anomalies du salarié connecté sur la période.</summary>
public sealed record SelfAnomaliesDto(SelfPeriodDto Periode, IReadOnlyList<SelfAnomalyDto> Anomalies);
