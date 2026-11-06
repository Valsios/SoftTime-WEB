using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Jeton de rafraîchissement (migration 004). Seule l'empreinte SHA-256 est stockée. Les jetons issus d'une même
/// connexion partagent une FAMILLE : la réutilisation d'un jeton déjà remplacé révoque toute la famille.
/// </summary>
[Table("T_REFRESH_TOKEN")]
public class T_REFRESH_TOKEN
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long ID { get; set; }

    public decimal IDRESPONSABLE { get; set; }

    [MaxLength(64)]
    public string TOKEN_HASH { get; set; } = string.Empty;

    public Guid FAMILLE { get; set; }

    public DateTime DATE_CREATION { get; set; }

    public DateTime DATE_EXPIRATION { get; set; }

    public DateTime? DATE_REVOCATION { get; set; }

    /// <summary>Empreinte du jeton qui l'a remplacé lors d'une rotation (null : révoqué sans remplacement).</summary>
    [MaxLength(64)]
    public string? REMPLACE_PAR { get; set; }
}
