namespace SoftTime.Domain.Entities.SoftTime;

public class PlanningSalarie
{
    public int Id { get; set; }

    public int IdSal { get; set; }

    public int? NoShift { get; set; }

    public DateTime? DateP { get; set; }

    public DateTime? Ha { get; set; }

    public TimeSpan? Pause { get; set; }

    public DateTime? Hd { get; set; }

    public bool? OffP { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }
}
