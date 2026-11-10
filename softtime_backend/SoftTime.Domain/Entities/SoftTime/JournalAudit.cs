namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Journal applicatif central, indépendant du journal historique <c>suivi_SUIVI</c>
/// alimenté par les déclencheurs de l'ancienne application.
/// </summary>
public class JournalAudit
{
    public long Id { get; set; }

    public DateTime DateUtc { get; set; }

    public string Tables { get; set; } = string.Empty;

    public string? Cles { get; set; }

    public string? Colonnes { get; set; }

    public string Actions { get; set; } = string.Empty;

    public string? Anciens { get; set; }

    public string? Nouveaux { get; set; }

    public string Users { get; set; } = string.Empty;

    public string? BaseSage { get; set; }

    public string? BasePointeuse { get; set; }

    public Guid Correlations { get; set; }

    public string Source { get; set; } = string.Empty;
}
