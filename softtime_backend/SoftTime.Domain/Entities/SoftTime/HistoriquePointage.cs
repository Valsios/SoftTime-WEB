namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Archive d'un pointage supprimé (purge ou remplacement). Correspond à l'ancienne table
/// <c>HistoriquePointageEmploye</c> du desktop : elle permet de consulter — et non d'auditer
/// colonne par colonne — ce qui a été retiré de <c>T_POINTAGE</c>.
/// </summary>
public class HistoriquePointage
{
    public int Id { get; set; }

    /// <summary>Identifiant de la ligne d'origine dans <c>T_POINTAGE</c>.</summary>
    public decimal? PointageId { get; set; }

    public string? NomBddSage { get; set; }

    public string? NomBddPointeuse { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DatePointage { get; set; }

    public TimeSpan? HeurePointage { get; set; }

    public DateTime? DateImportation { get; set; }

    public decimal? IdUniquePointage { get; set; }

    public string? TypePointage { get; set; }

    public DateTime DateSuppression { get; set; }
}
