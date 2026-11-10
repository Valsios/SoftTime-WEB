namespace SoftTime.Application.DTOs;

public sealed record MlTrainingOutcomeDto(
    string Etat,
    string Message,
    string? HsProbabiliteVersion,
    string? HsMontantVersion,
    MlMetricsDto? HsMetriques);
