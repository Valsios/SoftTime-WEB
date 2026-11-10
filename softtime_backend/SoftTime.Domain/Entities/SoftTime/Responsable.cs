namespace SoftTime.Domain.Entities.SoftTime;

public class Responsable
{
    public decimal IdResponsable { get; set; }

    public decimal IdRole { get; set; }

    public string? NomResponsable { get; set; }

    public string? Login { get; set; }

    public string? Password { get; set; }

    public string? Matricule { get; set; }

    /// <summary>Adresse d'envoi des identifiants (colonne ajoutée par <c>UserAccountConfiguration</c>).</summary>
    public string? Email { get; set; }

    /// <summary>Échecs de connexion consécutifs (migration 004), remis à zéro par une connexion réussie.</summary>
    public int TentativesEchouees { get; set; }

    /// <summary>Fin du verrouillage temporaire après trop d'échecs (UTC, migration 004).</summary>
    public DateTime? VerrouilleJusqua { get; set; }
}
