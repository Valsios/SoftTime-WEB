namespace SoftTime.Application.Services;

public sealed record MlDatasetBuildResult(int DailyRows, int WeeklyRows, DateTime From, DateTime To);
