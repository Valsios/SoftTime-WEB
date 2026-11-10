namespace SoftTime.Application.DTOs;

public record CanteenDto(int Id, string? Matricule, DateTime? DateDeb, DateTime? DateFin, int? Total)
{
    public CanteenDto() : this(default, default, default, default, default) { }
}
