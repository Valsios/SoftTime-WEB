namespace SoftTime.Domain.Entities.SoftTime;

public class T_POINTAGE
{
    public decimal IDPOINTAGE { get; set; }

    public string? NOM_BDD_SAGE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_POINTAGE { get; set; }

    public TimeSpan? HEURE_POINTAGE { get; set; }

    public DateTime? DATE_IMPORTATION { get; set; }

    public string? NOM_BDD_POINTEUSE { get; set; }

    public decimal? IDUNIQUE_POINTAGE { get; set; }

    public string? TYPE_POINTAGE { get; set; }
}
