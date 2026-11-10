namespace SoftTime.Application.DTOs;

public record RoleDetailedPermissionsDto(
    decimal RoleId,
    IReadOnlyList<string> GrantedPermissions,
    bool IsConfigured
)
{
    public RoleDetailedPermissionsDto() : this(0, Array.Empty<string>(), false) { }
}
