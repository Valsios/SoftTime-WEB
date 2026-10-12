using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Archive d'une ligne de calcul EXO/IMPO supprimée (purge d'une période).
/// Correspond à l'ancienne table <c>HistoriqueHs_ExoImp</c> du desktop.
/// </summary>
[Table("T_HISTORIQUE_HS")]
public class T_HISTORIQUE_HS
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    /// <summary>Identifiant de la ligne d'origine dans <c>T_HSExoImp</c>.</summary>
    public int? HS_ID { get; set; }

    public string? MATRICULE { get; set; }

    public int? NUM_SAL { get; set; }

    public decimal? EXO130 { get; set; }

    public decimal? EXO150 { get; set; }

    public decimal? I130 { get; set; }

    public decimal? I150 { get; set; }

    public DateTime? PERIODE_DEBUT { get; set; }

    public DateTime? PERIODE_FIN { get; set; }

    public decimal? FERIE { get; set; }

    public decimal? NUIT { get; set; }

    public decimal? DIM { get; set; }

    public decimal? RETARD { get; set; }

    public decimal? ABSENCE { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public DateTime DATE_SUPPRESSION { get; set; }
}
