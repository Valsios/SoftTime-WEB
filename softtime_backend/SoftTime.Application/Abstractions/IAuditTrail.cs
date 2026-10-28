namespace SoftTime.Application.Abstractions;

/// <summary>Journal explicite des opérations réalisées hors de SoftTimeDbContext.</summary>
public interface IAuditTrail
{
    Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default);
}

public sealed record PayrollSyncAudit(
    string TargetTable,
    string? SageDatabase,
    DateTime PeriodFrom,
    DateTime PeriodTo,
    int UpdatedRows,
    IReadOnlyDictionary<string, int> UpdatesByCategory,
    string Status,
    string? UserLogin,
    string Source,
    Guid Correlation);

public sealed record PayrollSyncContext(
    DateTime PeriodFrom,
    DateTime PeriodTo,
    string? UserLogin,
    Guid Correlation);

/// <summary>Préserve le nombre de commandes déjà exécutées lorsqu'une synchro SQL échoue.</summary>
public sealed class PayrollSyncWriteException : Exception
{
    public PayrollSyncWriteException(
        Exception innerException,
        int updatedRows,
        IReadOnlyDictionary<string, int> updatesByCategory)
        : base(innerException.Message, innerException)
    {
        UpdatedRows = updatedRows;
        UpdatesByCategory = updatesByCategory;
    }

    public int UpdatedRows { get; }
    public IReadOnlyDictionary<string, int> UpdatesByCategory { get; }
}
