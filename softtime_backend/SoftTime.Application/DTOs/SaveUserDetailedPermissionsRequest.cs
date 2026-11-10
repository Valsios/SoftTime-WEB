namespace SoftTime.Application.DTOs;

public record SaveUserDetailedPermissionsRequest(
    decimal UserId,
    IReadOnlyList<string> GrantedPermissions
)
{
    public SaveUserDetailedPermissionsRequest() : this(0, Array.Empty<string>()) { }
}
