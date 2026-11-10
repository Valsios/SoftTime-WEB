namespace SoftTime.Domain.Entities.SoftTime;

public class Cantine
{
    public int IdCantine { get; set; }

    public string? NomBddSage { get; set; }

    public string? NomBddPointeuse { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DateDeb { get; set; }

    public DateTime? DateFin { get; set; }

    public int? TotalCantine { get; set; }
}
