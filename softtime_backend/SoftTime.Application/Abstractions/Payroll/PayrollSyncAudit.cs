namespace SoftTime.Application.Abstractions.Payroll;

public sealed record PayrollSyncAudit(
    string TargetTable,
    string? SageDatabase,
    DateTime PeriodFrom,
    DateTime PeriodTo,
    int Employees,
    int UpdatedRows,
    int CancelledRows,
    IReadOnlyDictionary<string, int> UpdatesByCategory,
    string Status,
    string? UserLogin,
    string Source,
    Guid Correlation);
