namespace SoftTime.Domain.Entities.SoftTime;

public class T_PLANNING
{
    public decimal IDPLANNING { get; set; }

    public int? NoSHIFT { get; set; }

    public TimeSpan? HA { get; set; }

    public TimeSpan? PAUSE { get; set; }

    public TimeSpan? HD { get; set; }

    public string? Intitule { get; set; }
}
