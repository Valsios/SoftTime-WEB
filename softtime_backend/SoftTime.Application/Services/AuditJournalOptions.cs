namespace SoftTime.Application.Services;

/// <summary>Section « AuditJournal » : journal des modifications faites par SoftTimeDbContext.</summary>
public sealed class AuditJournalOptions
{
    public const string Section = "AuditJournal";

    public const int DefaultDetailThreshold = 200;

    public bool Enabled { get; set; } = true;

    /// <summary>Au-delà de ce nombre de lignes modifiées, le journal ne garde qu'un résumé. Une valeur ≤ 0 vaut le défaut.</summary>
    public int DetailThreshold { get; set; } = DefaultDetailThreshold;

    /// <summary>Remplace un seuil invalide par la valeur par défaut, comme le faisait la lecture historique.</summary>
    public void Normalize()
    {
        if (DetailThreshold <= 0)
            DetailThreshold = DefaultDetailThreshold;
    }
}
