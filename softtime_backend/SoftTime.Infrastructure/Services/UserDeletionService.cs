using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class UserDeletionService : IUserDeletionService
{
    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IParametersAccessGuard _parametersAccessGuard;

    public UserDeletionService(
        IUnitOfWork uow,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard,
        IRefreshTokenService? refreshTokens = null,
        IOptions<AccountSecurityOptions>? securityOptions = null)
    {
        _uow = uow;
        _effectiveRights = effectiveRights;
        _refreshTokens = refreshTokens ?? new RefreshTokenService(uow, securityOptions);
        _parametersAccessGuard = parametersAccessGuard;
    }

    public async Task DeleteUserAsync(decimal currentUserId, decimal id, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        var repo = _uow.Repository<Responsable>();
        var caller = await repo.GetByIdAsync(currentUserId, cancellationToken);
        if (caller == null)
            throw new DomainException("Utilisateur appelant introuvable.");

        var callerRole = await _uow.Repository<Role>().GetByIdAsync(caller.IdRole, cancellationToken);
        if (callerRole == null)
            throw new DomainException("Utilisateur appelant introuvable.");

        var callerIsSuperAdmin = AdminRules.IsSuperAdmin(caller.IdRole);

        if (currentUserId == id)
            throw new DomainException("Vous ne pouvez pas supprimer votre propre compte.");

        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();

        await EnsureAdminDeletableAsync(repo, callerIsSuperAdmin, entity, cancellationToken);

        await RemoveAccountRightsAsync(entity.IdResponsable, cancellationToken);
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(id);
    }

    /// <summary>Un compte administrateur ne peut être supprimé que par un super-administrateur, et jamais s'il est le dernier.</summary>
    private static async Task EnsureAdminDeletableAsync(
        IRepository<Responsable> repo, bool callerIsSuperAdmin, Responsable entity, CancellationToken cancellationToken)
    {
        if (entity.IdRole == AdminRules.AdminRoleId)
        {
            if (!callerIsSuperAdmin)
                throw new DomainException("Seul un super-administrateur peut supprimer un compte administrateur.");
        }

        if (entity.IdRole == AdminRules.AdminRoleId)
        {
            var otherAdmins = await repo.ListAsync(u => u.IdRole == AdminRules.AdminRoleId && u.IdResponsable != entity.IdResponsable, cancellationToken);
            if (otherAdmins.Count == 0)
                throw new ConflictException("Impossible de supprimer le dernier administrateur.");
        }
    }

    /// <summary>Réservé aux tests (internal, sans garde) : le code de production doit transmettre l'identité de l'appelant.</summary>
    internal async Task DeleteUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<Responsable>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();
        if (entity.IdRole == AdminRules.AdminRoleId)
        {
            var otherAdmins = await repo.ListAsync(u => u.IdRole == AdminRules.AdminRoleId && u.IdResponsable != entity.IdResponsable, cancellationToken);
            if (otherAdmins.Count == 0)
                throw new ConflictException("Impossible de supprimer le dernier administrateur.");
        }
        await RemoveAccountRightsAsync(entity.IdResponsable, cancellationToken);
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(id);
    }

    /// <summary>
    /// Met en attente la suppression des droits personnels et des accès aux bases d'un compte supprimé.
    /// Le journal d'audit (T_AUDIT_DROIT) est conservé. Aucun SaveChanges ici : tout part avec la suppression du compte,
    /// dans la même transaction.
    /// </summary>
    private async Task RemoveAccountRightsAsync(decimal userId, CancellationToken cancellationToken)
    {
        _effectiveRights.Invalidate(userId);
        await _refreshTokens.RemoveAllAsync(userId, cancellationToken);
        var permissions = _uow.Repository<UserPermission>();
        permissions.RemoveRange(await permissions.ListAsync(p => p.IdResponsable == userId, cancellationToken));
        var access = _uow.Repository<BddAutorisee>();
        access.RemoveRange(await access.ListAsync(a => a.IdResponsable == userId, cancellationToken));
    }
}
