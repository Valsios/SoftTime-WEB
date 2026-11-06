namespace SoftTime.Domain.Entities.SoftTime;

public class Frais
{
    public int IdPointage { get; set; }

    public string? NomBddSage { get; set; }

    public string? NomBddPointeuse { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DateDeb { get; set; }

    public DateTime? DateFin { get; set; }

    public int? TotalPoint { get; set; }
}
