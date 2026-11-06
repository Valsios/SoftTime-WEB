global using SoftTime.Domain.Models;

namespace SoftTime.Application.DTOs;

public record AbsenceCodeDto(int Id, string? Intitule, bool? NotPay)
{
    public AbsenceCodeDto() : this(default, default, default) { }
}
