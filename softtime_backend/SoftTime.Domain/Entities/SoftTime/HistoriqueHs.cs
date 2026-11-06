namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Archive d'une ligne de calcul EXO/IMPO supprimée (purge d'une période).
/// Correspond à l'ancienne table <c>HistoriqueHs_ExoImp</c> du desktop.
/// </summary>
public class HistoriqueHs
{
    public int Id { get; set; }

    /// <summary>Identifiant de la ligne d'origine dans <c>T_HSExoImp</c>.</summary>
    public int? HsId { get; set; }

    public string? Matricule { get; set; }

    public int? NumSal { get; set; }

    public decimal? Exo130 { get; set; }

    public decimal? Exo150 { get; set; }

    public decimal? I130 { get; set; }

    public decimal? I150 { get; set; }

    public DateTime? PeriodeDebut { get; set; }

    public DateTime? PeriodeFin { get; set; }

    public decimal? Ferie { get; set; }

    public decimal? Nuit { get; set; }

    public decimal? Dim { get; set; }

    public decimal? Retard { get; set; }

    public decimal? Absence { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public DateTime DateSuppression { get; set; }
}
