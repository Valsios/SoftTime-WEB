using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Domain;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="AuthService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IAuthService
{
    /// <summary>
    /// Consultation sensible : administrateur, ou porte 2 ET l'une des permissions fines, lues dans les droits effectifs
    /// (pas dans le jeton). Échec fermé si la lecture des droits échoue.
    /// </summary>
    Task EnsureCanReadAsync(string[] anyOf, CancellationToken cancellationToken = default);

    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rafraîchit la session : le jeton présenté est échangé contre un nouveau (rotation) et le jeton d'accès
    /// est réémis avec les droits effectifs actuels (un droit retiré disparaît au plus tard au rafraîchissement suivant).
    /// </summary>
    Task<LoginResponse> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Déconnexion : le jeton de rafraîchissement présenté ne peut plus servir.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    Task<LoginResponse> GetMeAsync(decimal userId, CancellationToken cancellationToken = default);

    Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserDto>> GetUsersPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installation vierge ? Portage de <c>is-first-run</c> du desktop, avec un critère plus sain :
    /// le desktop cherchait un rôle nommé « administrateur », ici on constate simplement qu'aucun
    /// compte n'existe. Le critère ne dépend donc pas d'une convention de nommage des rôles, et il
    /// reste vrai après la suppression du dernier compte (seul moyen de sortir d'un verrouillage total).
    /// </summary>
    Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée le premier compte administrateur. Refusé dès qu'un compte existe, et le rôle
    /// <see cref="RoleAdministrateur"/> se voit garantir tous les droits : sans cela, l'application
    /// serait inaccessible juste après son installation.
    /// </summary>
    Task<AdminSetupResultDto> CreateAdminAsync(AdminSetupDto dto, CancellationToken cancellationToken = default);

    Task<UserDto> SaveUserAsync(decimal currentUserId, UserDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aperçu d'un changement de rôle : ajustements personnels qui seraient effacés. Lecture seule, administrateur seulement.
    /// Si le rôle ne change pas ou si le compte n'a aucun ajustement, Count vaut 0.
    /// </summary>
    Task<RoleChangePreviewDto> PreviewRoleChangeAsync(decimal userId, decimal newRoleId, CancellationToken cancellationToken = default);

    Task DeleteUserAsync(decimal currentUserId, decimal id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<RoleDto>> GetRolesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default);

    Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default);

    Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default);

    Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default);

    Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default);

    Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default);

    Task<RoleDetailedPermissionsDto> GetRoleDetailedPermissionsAsync(decimal roleId, CancellationToken cancellationToken = default);

    Task SaveRoleDetailedPermissionsAsync(decimal currentUserId, decimal targetRoleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default);

    Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditDroitDto>> GetAuditDroitsAsync(string? typeCible, decimal? idCible, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
}
