namespace SoftTime.Domain.Entities.SoftTime;

public class T_PLANNING_SAL
{
    public int ID { get; set; }

    public int IDSAL { get; set; }

    public int? NoSHIFT { get; set; }

    public DateTime? DateP { get; set; }

    public DateTime? HA { get; set; }

    public TimeSpan? Pause { get; set; }

    public DateTime? HD { get; set; }

    public bool? OffP { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }
}
