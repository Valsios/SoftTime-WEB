namespace SoftTime.Application.Abstractions.Payroll;

public sealed record PayrollSyncContext(
    DateTime PeriodFrom,
    DateTime PeriodTo,
    string? UserLogin,
    Guid Correlation)
{
    /// <summary>Filtre de la sélection envoyée (département, matricules) : figé dans l'instantané de l'envoi.</summary>
    public PayrollSendFilter Filtre { get; init; } = PayrollSendFilter.Aucun;
}
