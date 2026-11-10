namespace SoftTime.Domain.Services;

/// <summary>Statut de clôture d'une période, calculé à partir des anomalies CRITIQUES.</summary>
public enum ClosingStatus
{
    Pret,
    ACorriger,
    Bloquant
}
