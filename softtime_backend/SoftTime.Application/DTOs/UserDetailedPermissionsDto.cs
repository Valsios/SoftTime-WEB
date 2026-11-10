namespace SoftTime.Application.DTOs;

public record UserDetailedPermissionsDto(
    decimal UserId,
    IReadOnlyList<string> GrantedPermissions,
    bool IsCustomized
)
{
    public UserDetailedPermissionsDto() : this(0, Array.Empty<string>(), false) { }

    /// <summary>Chaque droit avec sa source (rempli par l'écran Privilèges, vide ailleurs).</summary>
    public IReadOnlyList<EffectivePermissionDto> Rights { get; init; } = Array.Empty<EffectivePermissionDto>();
}
