namespace SoftTime.Application.Abstractions.Payroll;

public sealed record PayrollSyncContext(
    DateTime PeriodFrom,
    DateTime PeriodTo,
    string? UserLogin,
    Guid Correlation);
