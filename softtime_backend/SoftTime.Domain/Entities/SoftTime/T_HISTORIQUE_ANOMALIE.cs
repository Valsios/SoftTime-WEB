using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Archive d'une anomalie supprimée (arbitrage validé ou suppression manuelle).
/// Correspond à l'ancienne table <c>HistoriqueHeureAnomalie</c> du desktop.
/// </summary>
[Table("T_HISTORIQUE_ANOMALIE")]
public class T_HISTORIQUE_ANOMALIE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    /// <summary>Identifiant de la ligne d'origine dans <c>T_HEUREANOMALIE</c>.</summary>
    public decimal? ANOMALIE_ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_POINTAGE_IN { get; set; }

    public DateTime? DATE_POINTAGE_OUT { get; set; }

    public TimeSpan? H1 { get; set; }

    public TimeSpan? H2 { get; set; }

    public TimeSpan? HENTREE { get; set; }

    public TimeSpan? HPS { get; set; }

    public TimeSpan? HPE { get; set; }

    public TimeSpan? HSORTIE { get; set; }

    public bool? ABS_AM { get; set; }

    public bool? ABS_PM { get; set; }

    public bool? FERIES { get; set; }

    public bool? FERIES_AM { get; set; }

    public bool? FERIES_PM { get; set; }

    public string? INTITULE_ABSENCE { get; set; }

    public DateTime DATE_SUPPRESSION { get; set; }
}
