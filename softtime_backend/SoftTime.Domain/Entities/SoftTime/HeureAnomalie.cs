namespace SoftTime.Domain.Entities.SoftTime;

public class HeureAnomalie
{
    public decimal Id { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DatePointageIn { get; set; }

    public DateTime? DatePointageOut { get; set; }

    public TimeSpan? H1 { get; set; }

    public TimeSpan? H2 { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public TimeSpan? HEntree { get; set; }

    public TimeSpan? HSortie { get; set; }

    public bool? AbsAm { get; set; }

    public TimeSpan? Hpe { get; set; }

    public TimeSpan? Hps { get; set; }

    public bool? FeriesAm { get; set; }

    public bool? AbsPm { get; set; }

    public bool? FeriesPm { get; set; }

    public bool? Feries { get; set; }

    public string? IntituleAbsence { get; set; }
}
