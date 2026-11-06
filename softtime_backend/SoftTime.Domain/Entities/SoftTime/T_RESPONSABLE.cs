namespace SoftTime.Domain.Entities.SoftTime;

public class T_RESPONSABLE
{
    public decimal IDRESPONSABLE { get; set; }

    public decimal IDROLE { get; set; }

    public string? NOMRESPONSABLE { get; set; }

    public string? LOGIN { get; set; }

    public string? PASSWORD { get; set; }

    public string? MATRICULE { get; set; }

    /// <summary>Adresse d'envoi des identifiants (colonne ajoutée par <c>UserAccountConfiguration</c>).</summary>
    public string? EMAIL { get; set; }

    /// <summary>Échecs de connexion consécutifs (migration 004), remis à zéro par une connexion réussie.</summary>
    public int TENTATIVES_ECHOUEES { get; set; }

    /// <summary>Fin du verrouillage temporaire après trop d'échecs (UTC, migration 004).</summary>
    public DateTime? VERROUILLE_JUSQUA { get; set; }
}
