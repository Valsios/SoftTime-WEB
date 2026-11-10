namespace SoftTime.Application.DTOs;

public sealed record MlPipelineResultDto(
    string Etat,
    string Message,
    int DailyRows,
    int WeeklyRows,
    int HsPredictions);
