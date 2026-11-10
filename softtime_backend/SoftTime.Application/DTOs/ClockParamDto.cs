namespace SoftTime.Application.DTOs;

public record ClockParamDto(int Id, bool? MultiPoint)
{
    public ClockParamDto() : this(default, default) { }
}
