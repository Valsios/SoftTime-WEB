namespace SoftTime.Domain.Entities.SoftTime;

public class ToleranceRetard
{
    public int Id { get; set; }

    public int IdCategorie { get; set; }

    public int Tolerance { get; set; }

    public bool TypesTol { get; set; }

    public int? Sortie { get; set; }
}
