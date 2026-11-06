namespace SoftTime.Application.Abstractions;

public sealed record PayrollSyncContext(
    DateTime PeriodFrom,
    DateTime PeriodTo,
    string? UserLogin,
    Guid Correlation);
