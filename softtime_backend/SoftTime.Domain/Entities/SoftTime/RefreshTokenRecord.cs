namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Jeton de rafraîchissement (migration 004). Seule l'empreinte SHA-256 est stockée. Les jetons issus d'une même
/// connexion partagent une FAMILLE : la réutilisation d'un jeton déjà remplacé révoque toute la famille.
/// </summary>
public class RefreshTokenRecord
{
    public long Id { get; set; }

    public decimal IdResponsable { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public Guid Famille { get; set; }

    public DateTime DateCreation { get; set; }

    public DateTime DateExpiration { get; set; }

    public DateTime? DateRevocation { get; set; }

    /// <summary>Empreinte du jeton qui l'a remplacé lors d'une rotation (null : révoqué sans remplacement).</summary>
    public string? RemplacePar { get; set; }
}
