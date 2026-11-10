namespace SoftTime.Application.DTOs;

public record ToleranceDto(int Id, int CategoryId, int Tolerance, bool TypesTol, int? Sortie)
{
    public ToleranceDto() : this(default, default, default, default, default) { }
}
