namespace SoftTime.Application.DTOs;

public record MajorationDto(int Id, string? Mojoration, decimal? Cotation)
{
    public MajorationDto() : this(default, default, default) { }
}
