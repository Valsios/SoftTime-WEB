namespace SoftTime.Application.DTOs;

public record HolidayDto(int Id, string? Intitule, DateTime? Date)
{
    public HolidayDto() : this(default, default, default) { }
}
