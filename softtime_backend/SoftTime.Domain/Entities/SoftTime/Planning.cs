namespace SoftTime.Domain.Entities.SoftTime;

public class Planning
{
    public decimal IdPlanning { get; set; }

    public int? NoShift { get; set; }

    public TimeSpan? Ha { get; set; }

    public TimeSpan? Pause { get; set; }

    public TimeSpan? Hd { get; set; }

    public string? Intitule { get; set; }
}
