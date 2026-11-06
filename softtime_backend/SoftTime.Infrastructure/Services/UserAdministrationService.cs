using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class UserAdministrationService : IUserAdministrationService
{
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _email;
    private readonly IPasswordHasher<Responsable> _passwordHasher;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IParametersAccessGuard _parametersAccessGuard;
    private readonly IRightsChangeJournal _rightsChangeJournal;

    public UserAdministrationService(
        IUnitOfWork uow,
        IEmailService email,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard,
        IRightsChangeJournal rightsChangeJournal,
        IPasswordHasher<Responsable>? passwordHasher = null,
        IRefreshTokenService? refreshTokens = null,
        IOptions<AccountSecurityOptions>? securityOptions = null)
    {
        _uow = uow;
        _email = email;
        _passwordHasher = passwordHasher ?? new PasswordHasher<Responsable>();
        _effectiveRights = effectiveRights;
        _refreshTokens = refreshTokens ?? new RefreshTokenService(uow, securityOptions);
        _parametersAccessGuard = parametersAccessGuard;
        _rightsChangeJournal = rightsChangeJournal;
    }

    public async Task<UserDto> SaveUserAsync(decimal currentUserId, UserDto dto, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        ValidateUserFields(dto);
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new DomainException("Utilisateur appelant introuvable.");
        _ = await _uow.Repository<Role>().GetByIdAsync(caller.IdRole, cancellationToken)
            ?? throw new DomainException("Utilisateur appelant introuvable.");
        var callerIsSuperAdmin = AdminRules.IsSuperAdmin(caller.IdRole);

        if (dto.Id == 0)
        {
            EnsureCanAssignRole(dto.RoleId, callerIsSuperAdmin);
            return await ExecuteSaveUserInternalAsync(null, dto, cancellationToken);
        }

        var entity = await _uow.Repository<Responsable>().GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
        if (dto.Id == currentUserId && entity.IdRole != dto.RoleId)
            throw new DomainException("Vous ne pouvez pas modifier votre propre rôle.");
        if (entity.IdRole == AdminRules.AdminRoleId && !callerIsSuperAdmin)
            throw new DomainException("Seul un super-administrateur peut modifier un compte administrateur.");
        EnsureCanAssignRole(dto.RoleId, callerIsSuperAdmin);
        await EnsureNotLastAdminAsync(entity, dto.RoleId, cancellationToken);
        if (entity.IdRole != dto.RoleId)
            await ClearAdjustmentsForRoleChangeAsync(caller, entity, dto.ConfirmClearAdjustments, cancellationToken);

        return await ExecuteSaveUserInternalAsync(entity, dto, cancellationToken);
    }

    /// <summary>Réservé aux tests (internal, sans garde) : le code de production doit transmettre l'identité de l'appelant.</summary>
    internal async Task<UserDto> SaveUserAsync(UserDto dto, CancellationToken cancellationToken = default)
    {
        Responsable? entity = null;
        if (dto.Id != 0)
        {
            entity = await _uow.Repository<Responsable>().GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
            await EnsureNotLastAdminAsync(entity, dto.RoleId, cancellationToken);
        }
        return await ExecuteSaveUserInternalAsync(entity, dto, cancellationToken);
    }

    /// <summary>
    /// Aperçu d'un changement de rôle : ajustements personnels qui seraient effacés. Lecture seule, administrateur seulement.
    /// Si le rôle ne change pas ou si le compte n'a aucun ajustement, Count vaut 0.
    /// </summary>
    public async Task<RoleChangePreviewDto> PreviewRoleChangeAsync(decimal userId, decimal newRoleId, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException();
        var login = user.Login ?? string.Empty;
        if (user.IdRole == newRoleId)
            return new RoleChangePreviewDto(Array.Empty<string>(), 0, login);
        var adjustments = await _effectiveRights.AdjustmentsAsync(userId, cancellationToken);
        return new RoleChangePreviewDto(adjustments, adjustments.Count, login);
    }

    private static void EnsureCanAssignRole(decimal roleId, bool callerIsSuperAdmin)
    {
        if (roleId == AdminRules.AdminRoleId && !callerIsSuperAdmin)
            throw new DomainException("Seul un super-administrateur peut attribuer le rôle Administrateur.");
    }

    private async Task EnsureNotLastAdminAsync(Responsable entity, decimal newRoleId, CancellationToken ct)
    {
        if (entity.IdRole != AdminRules.AdminRoleId || newRoleId == AdminRules.AdminRoleId)
            return;
        var otherAdmins = await _uow.Repository<Responsable>().ListAsync(
            u => u.IdRole == AdminRules.AdminRoleId && u.IdResponsable != entity.IdResponsable, ct);
        if (otherAdmins.Count == 0)
            throw new DomainException("Impossible de rétrograder le dernier administrateur.");
    }

    /// <summary>
    /// Changement de rôle : les ajustements personnels (et toute ligne personnelle) disparaissent dans
    /// la même sauvegarde, après confirmation explicite s'il y en a. Lectures seulement avant la
    /// confirmation : un refus n'écrit rien.
    /// </summary>
    private async Task ClearAdjustmentsForRoleChangeAsync(
        Responsable caller, Responsable entity, bool confirmed, CancellationToken ct)
    {
        var adjustments = await _effectiveRights.AdjustmentsAsync(entity.IdResponsable, ct);
        if (adjustments.Count > 0 && !confirmed)
            throw new RoleChangeConfirmationRequiredException(
                new RoleChangePreviewDto(adjustments, adjustments.Count, entity.Login ?? string.Empty));

        var permRepo = _uow.Repository<UserPermission>();
        var existingPerms = await permRepo.ListAsync(p => p.IdResponsable == entity.IdResponsable, ct);
        if (existingPerms.Count == 0)
            return;
        var permsAvant = existingPerms
            .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();
        permRepo.RemoveRange(existingPerms);
        await _rightsChangeJournal.EcrireAuditDroitAsync(
            new RightsChange(caller.IdResponsable, caller.Login ?? string.Empty, "USER", entity.IdResponsable, entity.NomResponsable, "RESET_USER_PERMS", permsAvant, null),
            ct);
    }

    private async Task<UserDto> ExecuteSaveUserInternalAsync(Responsable? entity, UserDto dto, CancellationToken cancellationToken)
    {
        var (nom, login) = ValidateUserFields(dto);
        var loginModifie = false;
        var motDePasseModifie = false;
        if (dto.Id == 0)
        {
            entity = await CreateUserAsync(dto, nom, login, cancellationToken);
        }
        else
        {
            entity ??= await _uow.Repository<Responsable>().GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
            loginModifie = !string.Equals((entity.Login ?? string.Empty).Trim(), login, StringComparison.Ordinal);
            // Ne pas bloquer la modification d'un compte historique dont le login serait déjà dupliqué :
            // l'unicité n'est vérifiée que lorsqu'un nouveau login est choisi.
            if (!string.Equals((entity.Login ?? string.Empty).Trim(), login, StringComparison.OrdinalIgnoreCase))
                await EnsureLoginAvailableAsync(_uow.Repository<Responsable>(), login, dto.Id, cancellationToken);
            if (!string.IsNullOrWhiteSpace(dto.Password))
                motDePasseModifie = await UpdatePasswordAsync(entity, dto.Password, cancellationToken);
            ApplyFields(entity, dto, nom, login);
            _uow.Repository<Responsable>().Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(entity.IdResponsable);

        // Notification best-effort : le service journalise et n'échoue jamais la requête.
        if (dto.Id == 0)
            await _email.SendUserCredentialsAsync(new AccountMailRecipient(entity.Email, entity.Login, entity.NomResponsable, null), dto.Password, cancellationToken);
        else if (loginModifie || motDePasseModifie)
            await _email.SendUserUpdateAsync(new AccountMailRecipient(entity.Email, entity.Login, entity.NomResponsable, null), motDePasseModifie ? dto.Password : null, loginModifie, motDePasseModifie, cancellationToken);

        return new UserDto(entity.IdResponsable, entity.IdRole, entity.NomResponsable, entity.Login ?? string.Empty, entity.Matricule, null) { Email = entity.Email };
    }

    private async Task<Responsable> CreateUserAsync(UserDto dto, string nom, string login, CancellationToken ct)
    {
        await EnsureLoginAvailableAsync(_uow.Repository<Responsable>(), login, dto.Id, ct);
        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new FieldValidationException("password", "Le mot de passe est obligatoire pour un nouvel utilisateur.");
        EnsurePasswordPolicy(dto.Password);

        var entity = new Responsable { Password = _passwordHasher.HashPassword(null!, dto.Password) };
        ApplyFields(entity, dto, nom, login);
        await _uow.Repository<Responsable>().AddAsync(entity, ct);
        return entity;
    }

    private static void ApplyFields(Responsable entity, UserDto dto, string nom, string login)
    {
        entity.IdRole = dto.RoleId;
        entity.NomResponsable = nom;
        entity.Login = login;
        entity.Matricule = dto.Matricule;
        entity.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
    }

    /// <summary>
    /// Nouveau mot de passe : haché, compte déverrouillé et sessions ouvertes avec l'ancien fermées.
    /// Même mot de passe stocké en clair ou dans un ancien format : simplement re-haché au format
    /// courant. Retourne vrai si le mot de passe a changé.
    /// </summary>
    private async Task<bool> UpdatePasswordAsync(Responsable entity, string password, CancellationToken ct)
    {
        var (isSame, rehashNeeded) = VerifyCurrentPassword(entity, password);
        if (!isSame)
        {
            EnsurePasswordPolicy(password);
            entity.Password = _passwordHasher.HashPassword(entity, password);
            entity.TentativesEchouees = 0;
            entity.VerrouilleJusqua = null;
            await _refreshTokens.RevokeAllAsync(entity.IdResponsable, ct);
            return true;
        }
        if (rehashNeeded || PasswordHashClassifier.IsPlainText(entity.Password))
            entity.Password = _passwordHasher.HashPassword(entity, password);
        return false;
    }

    private (bool IsSame, bool RehashNeeded) VerifyCurrentPassword(Responsable entity, string password)
    {
        if (string.IsNullOrEmpty(entity.Password))
            return (false, false);
        try
        {
            var res = _passwordHasher.VerifyHashedPassword(entity, entity.Password, password);
            return (res != PasswordVerificationResult.Failed, res == PasswordVerificationResult.SuccessRehashNeeded);
        }
        catch (FormatException)
        {
            // Valeur historique en clair : comparaison à temps constant.
            return (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(entity.Password), Encoding.UTF8.GetBytes(password)), false);
        }
    }

    /// <summary>Valide le mot de passe et rattache l'erreur au champ « password » du formulaire.</summary>
    private static void EnsurePasswordPolicy(string? password)
    {
        var validation = PasswordPolicy.Validate(password);
        if (!validation.IsValid)
            throw new FieldValidationException("password", validation.ErrorMessage);
    }

    /// <summary>Champs obligatoires d'un compte (nom, login, rôle) : renvoie le nom et le login nettoyés.</summary>
    private static (string Nom, string Login) ValidateUserFields(UserDto dto)
    {
        var nom = (dto.Nom ?? string.Empty).Trim();
        var login = (dto.Login ?? string.Empty).Trim();

        if (nom.Length == 0)
            throw new FieldValidationException("nom", "Le nom est obligatoire.");
        if (login.Length == 0)
            throw new FieldValidationException("login", "Le login est obligatoire.");
        if (dto.RoleId <= 0)
            throw new FieldValidationException("roleId", "Le rôle est obligatoire.");
        return (nom, login);
    }

    /// <summary>Refuse un login déjà porté par un autre compte (comparaison sans casse ni espaces).</summary>
    private static async Task EnsureLoginAvailableAsync(
        IRepository<Responsable> repo,
        string login,
        decimal id,
        CancellationToken cancellationToken)
    {
        var autres = await repo.ListAsync(LoginKey.MatchesOther(LoginKey.Normalize(login), id), cancellationToken);
        if (autres.Count > 0)
            throw new FieldValidationException("login", LoginKey.TakenMessage);
    }
}
