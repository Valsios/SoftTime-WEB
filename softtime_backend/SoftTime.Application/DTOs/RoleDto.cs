namespace SoftTime.Application.DTOs;

public record RoleDto(decimal Id, string Nom)
{
    public RoleDto() : this(default, default!) { }
}
