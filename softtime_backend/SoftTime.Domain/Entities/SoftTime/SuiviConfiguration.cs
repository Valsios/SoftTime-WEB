namespace SoftTime.Domain.Entities.SoftTime;

public class SuiviConfiguration
{
    public int Id { get; set; }

    public string? Tables { get; set; }

    public string? Colonnes { get; set; }

    public string? Types { get; set; }

    public bool? Etats { get; set; }

    public string? Bases { get; set; }
}
