using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Users;

/// <summary>Droits d'un compte pour l'écran Privilèges : liste affichée, plus chaque droit avec sa source.</summary>
public interface IUserRightsDescriber
{
    Task<UserDetailedPermissionsDto> DescribeAsync(decimal userId, CancellationToken ct = default);
}
