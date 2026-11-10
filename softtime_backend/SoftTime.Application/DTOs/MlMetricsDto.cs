using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.DTOs;

public sealed record MlMetricsDto(
    double? Auc,
    double? PrAuc,
    double? Brier,
    double? Mae,
    double? Rmse,
    double? R2,
    double? BaselineAuc,
    double? BaselineMae,
    int TestRows,
    int TestPositives)
{
    public static MlMetricsDto? From(MlMetrics? metrics) => metrics is null
        ? null
        : new MlMetricsDto(
            metrics.Auc, metrics.PrAuc, metrics.Brier, metrics.Mae, metrics.Rmse, metrics.R2,
            metrics.BaselineAuc, metrics.BaselineMae, metrics.TestRows, metrics.TestPositives);
}
