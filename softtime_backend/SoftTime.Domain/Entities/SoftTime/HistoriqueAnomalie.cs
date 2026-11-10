namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Archive d'une anomalie supprimée (arbitrage validé ou suppression manuelle).
/// Correspond à l'ancienne table <c>HistoriqueHeureAnomalie</c> du desktop.
/// </summary>
public class HistoriqueAnomalie
{
    public int Id { get; set; }

    /// <summary>Identifiant de la ligne d'origine dans <c>T_HEUREANOMALIE</c>.</summary>
    public decimal? AnomalieId { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DatePointageIn { get; set; }

    public DateTime? DatePointageOut { get; set; }

    public TimeSpan? H1 { get; set; }

    public TimeSpan? H2 { get; set; }

    public TimeSpan? HEntree { get; set; }

    public TimeSpan? Hps { get; set; }

    public TimeSpan? Hpe { get; set; }

    public TimeSpan? HSortie { get; set; }

    public bool? AbsAm { get; set; }

    public bool? AbsPm { get; set; }

    public bool? Feries { get; set; }

    public bool? FeriesAm { get; set; }

    public bool? FeriesPm { get; set; }

    public string? IntituleAbsence { get; set; }

    public DateTime DateSuppression { get; set; }
}
