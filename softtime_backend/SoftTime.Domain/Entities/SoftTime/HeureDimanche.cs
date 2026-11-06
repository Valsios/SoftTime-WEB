namespace SoftTime.Domain.Entities.SoftTime;

public class HeureDimanche
{
    public int Id { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DateDim { get; set; }

    public TimeSpan? HeureDebut { get; set; }

    public TimeSpan? HeureFin { get; set; }

    public decimal? Valeur { get; set; }
}
