namespace SoftTime.Domain.Entities.SoftTime;

public class Pointage
{
    public decimal IdPointage { get; set; }

    public string? NomBddSage { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DatePointage { get; set; }

    public TimeSpan? HeurePointage { get; set; }

    public DateTime? DateImportation { get; set; }

    public string? NomBddPointeuse { get; set; }

    public decimal? IdUniquePointage { get; set; }

    public string? TypePointage { get; set; }
}
