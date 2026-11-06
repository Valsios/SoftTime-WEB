namespace SoftTime.Application.DTOs;

public record PunchDto(decimal Id, string? Matricule, DateTime? Date, TimeSpan? Heure, string? Type, DateTime? ImportDate)
{
    public PunchDto() : this(default, default, default, default, default, default) { }
}
