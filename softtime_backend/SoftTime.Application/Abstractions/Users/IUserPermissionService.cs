using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Users;

public interface IUserPermissionService
{
    Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default);

    Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default);

    Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Retire d'un compte des anciens codes de base sans action (« SAGE:X »), sur confirmation de l'administrateur.</summary>
    Task RemoveLegacyPermissionsAsync(
        decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);
}
