using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Journal applicatif central, indépendant du journal historique <c>suivi_SUIVI</c>
/// alimenté par les déclencheurs de l'ancienne application.
/// </summary>
[Table("suivi_JOURNAL")]
public class suivi_JOURNAL
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public DateTime DATE_UTC { get; set; }

    public string TABLES { get; set; } = string.Empty;

    public string? CLES { get; set; }

    public string? COLONNES { get; set; }

    public string ACTIONS { get; set; } = string.Empty;

    public string? ANCIENS { get; set; }

    public string? NOUVEAUX { get; set; }

    public string USERS { get; set; } = string.Empty;

    public string? BASE_SAGE { get; set; }

    public string? BASE_POINTEUSE { get; set; }

    public Guid CORRELATIONS { get; set; }

    public string SOURCE { get; set; } = string.Empty;
}
