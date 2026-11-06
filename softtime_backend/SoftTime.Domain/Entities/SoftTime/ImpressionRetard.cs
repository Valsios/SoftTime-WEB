namespace SoftTime.Domain.Entities.SoftTime;

public class ImpressionRetard
{
    public int Id { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public string? Matricules { get; set; }

    public string? DatePointage { get; set; }

    public TimeSpan? HeureEntree { get; set; }

    public TimeSpan? Retards { get; set; }
}
