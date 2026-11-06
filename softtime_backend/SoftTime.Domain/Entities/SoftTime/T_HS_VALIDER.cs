namespace SoftTime.Domain.Entities.SoftTime;

public class T_HS_VALIDER
{
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTAGE { get; set; }

    public string? MATRICULES { get; set; }

    public DateTime? DATE_DEB { get; set; }

    public DateTime? DATE_FIN { get; set; }

    public decimal? HT { get; set; }

    public decimal? HS { get; set; }

    public decimal? NUIT { get; set; }

    public decimal? DIMANCHE { get; set; }

    public decimal? FERIES { get; set; }

    public bool? VALIDATION_HS { get; set; }

    public decimal? RETARD { get; set; }

    public decimal? ABSENCE { get; set; }
}
