namespace SoftTime.Application.DTOs;

public record SaveRoleDetailedPermissionsRequest(
    IReadOnlyList<string> GrantedPermissions
)
{
    public SaveRoleDetailedPermissionsRequest() : this(Array.Empty<string>()) { }
}
