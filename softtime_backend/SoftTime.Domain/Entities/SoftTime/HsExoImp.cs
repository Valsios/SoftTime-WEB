namespace SoftTime.Domain.Entities.SoftTime;

public class HsExoImp
{
    public int Oid { get; set; }

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
}
