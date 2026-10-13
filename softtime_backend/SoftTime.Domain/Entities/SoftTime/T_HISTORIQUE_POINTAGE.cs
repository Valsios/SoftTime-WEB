using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Archive d'un pointage supprimé (purge ou remplacement). Correspond à l'ancienne table
/// <c>HistoriquePointageEmploye</c> du desktop : elle permet de consulter — et non d'auditer
/// colonne par colonne — ce qui a été retiré de <c>T_POINTAGE</c>.
/// </summary>
[Table("T_HISTORIQUE_POINTAGE")]
public class T_HISTORIQUE_POINTAGE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    /// <summary>Identifiant de la ligne d'origine dans <c>T_POINTAGE</c>.</summary>
    public decimal? POINTAGE_ID { get; set; }

    public string? NOM_BDD_SAGE { get; set; }

    public string? NOM_BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_POINTAGE { get; set; }

    public TimeSpan? HEURE_POINTAGE { get; set; }

    public DateTime? DATE_IMPORTATION { get; set; }

    public decimal? IDUNIQUE_POINTAGE { get; set; }

    public string? TYPE_POINTAGE { get; set; }

    public DateTime DATE_SUPPRESSION { get; set; }
}
