namespace SoftTime.Application.DTOs;

public record PrivilegeDto(decimal Id, decimal RoleId, decimal DroitId)
{
    public PrivilegeDto() : this(default, default, default) { }
}
