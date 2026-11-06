namespace SoftTime.Application.DTOs;

public record AffectationDto(int Id, int CardPaieId, int CategoryId)
{
    public AffectationDto() : this(default, default, default) { }
}
