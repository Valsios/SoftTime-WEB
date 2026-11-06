namespace SoftTime.Domain.Entities.SoftTime;

public class HsSemaine
{
    public int Oid { get; set; }

    public string? Matricule { get; set; }

    public int? SalNum { get; set; }

    public decimal? TotalHeure { get; set; }

    public decimal? Hs { get; set; }

    public decimal? Hs1 { get; set; }

    public decimal? Hs2 { get; set; }

    public DateTime? PeriodeDebut { get; set; }

    public DateTime? PeriodeFin { get; set; }

    public int? Semaine { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }
}
