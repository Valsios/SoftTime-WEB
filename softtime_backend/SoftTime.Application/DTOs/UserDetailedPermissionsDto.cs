namespace SoftTime.Application.DTOs;

public record UserDetailedPermissionsDto(
    decimal UserId,
    IReadOnlyList<string> GrantedPermissions,
    bool IsCustomized
)
{
    public UserDetailedPermissionsDto() : this(0, Array.Empty<string>(), false) { }
}
