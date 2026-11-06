namespace SoftTime.Application.DTOs;

public record CorrespondenceModeDto(bool Active)
{
    public CorrespondenceModeDto() : this(Active:default) { }
}
