namespace SoftTime.Application.DTOs;

public record ShiftDto(decimal Id, int? NoShift, TimeSpan? Ha, TimeSpan? Pause, TimeSpan? Hd, string? Intitule)
{
    public ShiftDto() : this(default, default, default, default, default, default) { }
}
