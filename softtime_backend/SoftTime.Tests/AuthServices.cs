using AutoMapper;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services.Authentication;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Infrastructure.Services.Users;

namespace SoftTime.Tests;

/// <summary>
/// Assemble les services d'authentification et d'administration des droits (ex-AuthService) avec l'ancien
/// constructeur, et délègue chaque méthode au service qui la porte : les tests existants restent inchangés.
/// </summary>
internal sealed class AuthServices : IUserPermissionService
{
    public AuthServices(
        IUnitOfWork uow,
        ITokenService tokens,
        IMapper mapper,
        IEmailService email,
        ICurrentUser currentUser,
        IEffectiveRightsService effectiveRights,
        IPasswordHasher<Responsable>? passwordHasher = null,
        IRefreshTokenService? refreshTokens = null,
        IOptions<AccountSecurityOptions>? securityOptions = null,
        ILogger<AuthenticationService>? logger = null)
    {
        Guard = new ParametersAccessGuard(currentUser, effectiveRights);
        var journal = new RightsChangeJournal(uow);
        UserPermissions = new UserPermissionService(uow, effectiveRights, Guard, journal);
        RolePermissions = new RolePermissionService(uow, effectiveRights, Guard, journal);
        Authentication = new AuthenticationService(
            uow, tokens, mapper, effectiveRights, UserPermissions, passwordHasher, refreshTokens, securityOptions, logger);
        FirstRun = new FirstRunService(uow, email, passwordHasher);
        Deletion = new UserDeletionService(uow, effectiveRights, Guard, refreshTokens, securityOptions);
        Directory = new UserDirectoryService(uow, Guard);
        Administration = new UserAdministrationService(
            uow, email, effectiveRights, Guard, journal, passwordHasher, refreshTokens, securityOptions);
        var operatorJournal = new CurrentOperatorRightsJournal(journal, currentUser);
        Roles = new RoleService(uow, effectiveRights, Guard, operatorJournal);
        Privileges = new PrivilegeService(uow, effectiveRights, Guard, operatorJournal);
        DatabaseAccess = new DatabaseAccessService(uow, effectiveRights, Guard, operatorJournal);
        RightsAudit = new RightsAuditService(uow);
    }

    public ParametersAccessGuard Guard { get; }

    public AuthenticationService Authentication { get; }

    public FirstRunService FirstRun { get; }

    public UserDeletionService Deletion { get; }

    public UserDirectoryService Directory { get; }

    public UserAdministrationService Administration { get; }

    public RoleService Roles { get; }

    public PrivilegeService Privileges { get; }

    public UserPermissionService UserPermissions { get; }

    public RolePermissionService RolePermissions { get; }

    public DatabaseAccessService DatabaseAccess { get; }

    public RightsAuditService RightsAudit { get; }

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        => Authentication.LoginAsync(request, cancellationToken);

    public Task<LoginResponse> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default)
        => Authentication.RefreshAsync(refreshToken, cancellationToken);

    public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
        => Authentication.LogoutAsync(refreshToken, cancellationToken);

    public Task<LoginResponse> GetMeAsync(decimal userId, CancellationToken cancellationToken = default)
        => Authentication.GetMeAsync(userId, cancellationToken);

    public Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default)
        => FirstRun.IsFirstRunAsync(cancellationToken);

    public Task<AdminSetupResultDto> CreateAdminAsync(AdminSetupDto dto, CancellationToken cancellationToken = default)
        => FirstRun.CreateAdminAsync(dto, cancellationToken);

    public Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default)
        => Directory.GetUserAsync(id, cancellationToken);

    public Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
        => Directory.GetUsersAsync(cancellationToken);

    public Task<PagedResult<UserDto>> GetUsersPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default)
        => Directory.GetUsersPagedAsync(page, pageSize, search, cancellationToken);

    public Task<UserDto> SaveUserAsync(decimal currentUserId, UserDto dto, CancellationToken cancellationToken = default)
        => Administration.SaveUserAsync(currentUserId, dto, cancellationToken);

    public Task<RoleChangePreviewDto> PreviewRoleChangeAsync(decimal userId, decimal newRoleId, CancellationToken cancellationToken = default)
        => Administration.PreviewRoleChangeAsync(userId, newRoleId, cancellationToken);

    public Task DeleteUserAsync(decimal currentUserId, decimal id, CancellationToken cancellationToken = default)
        => Deletion.DeleteUserAsync(currentUserId, id, cancellationToken);

    public Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
        => Roles.GetRolesAsync(cancellationToken);

    public Task<PagedResult<RoleDto>> GetRolesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default)
        => Roles.GetRolesPagedAsync(page, pageSize, search, cancellationToken);

    public Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default)
        => Roles.SaveRoleAsync(dto, cancellationToken);

    public Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default)
        => Roles.DeleteRoleAsync(id, cancellationToken);

    public Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default)
        => Privileges.GetDroitsAsync(cancellationToken);

    public Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default)
        => Privileges.GetPrivilegesAsync(roleId, cancellationToken);

    public Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default)
        => Privileges.SetPrivilegesAsync(roleId, droitIds, cancellationToken);

    public Task EnsureCanReadAsync(string[] anyOf, CancellationToken cancellationToken = default)
        => Guard.EnsureCanReadAsync(anyOf, cancellationToken);

    public void RequireAdministrator()
        => Guard.RequireAdministrator();

    public Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default)
        => UserPermissions.GetUserDetailedPermissionsAsync(userId, cancellationToken);

    public Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
        => UserPermissions.SaveUserDetailedPermissionsAsync(currentUserId, targetUserId, permissions, cancellationToken);

    public Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default)
        => UserPermissions.ResetUserDetailedPermissionsAsync(currentUserId, targetUserId, cancellationToken);

    public Task RemoveLegacyPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
        => UserPermissions.RemoveLegacyPermissionsAsync(currentUserId, targetUserId, codes, cancellationToken);

    public Task<RoleDetailedPermissionsDto> GetRoleDetailedPermissionsAsync(decimal roleId, CancellationToken cancellationToken = default)
        => RolePermissions.GetRoleDetailedPermissionsAsync(roleId, cancellationToken);

    public Task SaveRoleDetailedPermissionsAsync(decimal currentUserId, decimal targetRoleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
        => RolePermissions.SaveRoleDetailedPermissionsAsync(currentUserId, targetRoleId, permissions, cancellationToken);

    public Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default)
        => DatabaseAccess.GetDbAccessAsync(userId, cancellationToken);

    public Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default)
        => DatabaseAccess.SetDbAccessAsync(userId, sageDbIds, cancellationToken);

    public Task<IReadOnlyList<AuditDroitDto>> GetAuditDroitsAsync(string? typeCible, decimal? idCible, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        => RightsAudit.GetAuditDroitsAsync(typeCible, idCible, from, to, cancellationToken);

    public Task<UserDto> SaveUserAsync(UserDto dto, CancellationToken cancellationToken = default)
        => Administration.SaveUserAsync(dto, cancellationToken);

    public Task DeleteUserAsync(decimal id, CancellationToken cancellationToken = default)
        => Deletion.DeleteUserAsync(id, cancellationToken);
}
