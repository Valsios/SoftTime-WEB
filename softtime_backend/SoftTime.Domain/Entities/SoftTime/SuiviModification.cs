namespace SoftTime.Domain.Entities.SoftTime;

public class SuiviModification
{
    public int Id { get; set; }

    public string? Tables { get; set; }

    public string? Colonnes { get; set; }

    public string? Users { get; set; }

    public string? Actions { get; set; }

    public DateTime? Datetimes { get; set; }

    public string? Nouveaux { get; set; }

    public string? Anciens { get; set; }

    public string? Bases { get; set; }
}
