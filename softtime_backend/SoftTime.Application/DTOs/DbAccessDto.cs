namespace SoftTime.Application.DTOs;

public record DbAccessDto(int Id, decimal UserId, int SageDbId)
{
    public DbAccessDto() : this(default, default, default) { }
}
