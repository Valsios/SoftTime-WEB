namespace SoftTime.Application.Abstractions.Payroll;

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
