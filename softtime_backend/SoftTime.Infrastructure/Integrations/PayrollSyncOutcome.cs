namespace SoftTime.Infrastructure.Integrations;

/// <summary>Bilan d'une synchronisation de paie à journaliser : volumes, lignes par catégorie et statut.</summary>
internal sealed record PayrollSyncOutcome(
    int Employees, int UpdatedRows, int CancelledRows, IReadOnlyDictionary<string, int> Categories, string Status);
