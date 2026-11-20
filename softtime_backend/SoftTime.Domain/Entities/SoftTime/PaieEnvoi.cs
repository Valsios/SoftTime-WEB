namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// T_PAIE_ENVOI : marqueur « période envoyée en paie », écrit par les writers de paie indépendamment du journal
/// central. Une ligne existe dès qu'au moins une valeur a pu atteindre la paie (un échec partiel STANDARD compte).
/// </summary>
public class PaieEnvoi
{
    public int Id { get; set; }

    public string BddSage { get; set; } = string.Empty;

    public DateTime PeriodeDebut { get; set; }

    public DateTime PeriodeFin { get; set; }

    /// <summary>Date UTC de l'envoi.</summary>
    public DateTime EnvoyeLe { get; set; }

    public string? EnvoyePar { get; set; }

    /// <summary>« EN_COURS », « SUCCES », « ECHEC » ou « INCERTAIN ».</summary>
    public string Statut { get; set; } = string.Empty;

    /// <summary>Lignes de paie mises à jour connues au dernier enregistrement.</summary>
    public int Lignes { get; set; }

    /// <summary>« STANDARD » ou « AUTRE ».</summary>
    public string TypeBase { get; set; } = string.Empty;

    /// <summary>Domaine de l'envoi (migration 011) : « HEURES », « CANTINE » ou « FRAIS » (<c>PayrollDomains</c>).</summary>
    public string Domaine { get; set; } = Models.PayrollDomains.Heures;
}
