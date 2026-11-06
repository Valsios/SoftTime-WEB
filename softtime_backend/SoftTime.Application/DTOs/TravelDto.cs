namespace SoftTime.Application.DTOs;

public record TravelDto(int Id, string? Matricule, DateTime? DateDeb, DateTime? DateFin, int? Total)
{
    public TravelDto() : this(default, default, default, default, default) { }
}
