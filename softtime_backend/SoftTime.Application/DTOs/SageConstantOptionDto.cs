namespace SoftTime.Application.DTOs;

public record SageConstantOptionDto(string Code, string? Intitule)
{
    public SageConstantOptionDto() : this(default!, default) { }
}
