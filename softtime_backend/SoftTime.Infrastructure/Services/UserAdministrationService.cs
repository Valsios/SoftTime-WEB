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
        var repo = _uow.Repository<Responsable>();
        var caller = await repo.GetByIdAsync(currentUserId, cancellationToken);
        if (caller == null)
            throw new DomainException("Utilisateur appelant introuvable.");

        var callerRole = await _uow.Repository<Role>().GetByIdAsync(caller.IdRole, cancellationToken);
        if (callerRole == null)
            throw new DomainException("Utilisateur appelant introuvable.");

        var callerIsSuperAdmin = AdminRules.IsSuperAdmin(caller.IdRole);

        Responsable? entity = null;
        if (dto.Id == 0)
        {
            if (dto.RoleId == AdminRules.AdminRoleId)
            {
                if (!callerIsSuperAdmin)
                    throw new DomainException("Seul un super-administrateur peut attribuer le rôle Administrateur.");
            }
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();

            if (dto.Id == currentUserId && entity.IdRole != dto.RoleId)
                throw new DomainException("Vous ne pouvez pas modifier votre propre rôle.");

            if (entity.IdRole == AdminRules.AdminRoleId)
            {
                if (!callerIsSuperAdmin)
                    throw new DomainException("Seul un super-administrateur peut modifier un compte administrateur.");
            }

            if (dto.RoleId == AdminRules.AdminRoleId)
            {
                if (!callerIsSuperAdmin)
                    throw new DomainException("Seul un super-administrateur peut attribuer le rôle Administrateur.");
            }

            if (entity.IdRole == AdminRules.AdminRoleId && dto.RoleId != AdminRules.AdminRoleId)
            {
                var otherAdmins = await repo.ListAsync(u => u.IdRole == AdminRules.AdminRoleId && u.IdResponsable != entity.IdResponsable, cancellationToken);
                if (otherAdmins.Count == 0)
                    throw new DomainException("Impossible de rétrograder le dernier administrateur.");
            }

            if (entity.IdRole != dto.RoleId)
            {
                // Lectures seulement jusqu'ici : un refus de confirmation n'écrit rien.
                var adjustments = await _effectiveRights.AdjustmentsAsync(entity.IdResponsable, cancellationToken);
                if (adjustments.Count > 0 && !dto.ConfirmClearAdjustments)
                    throw new RoleChangeConfirmationRequiredException(
                        new RoleChangePreviewDto(adjustments, adjustments.Count, entity.Login ?? string.Empty));

                // Les ajustements (et toute ligne personnelle) disparaissent dans la même sauvegarde que le changement de rôle.
                var permRepo = _uow.Repository<UserPermission>();
                var existingPerms = await permRepo.ListAsync(p => p.IdResponsable == entity.IdResponsable, cancellationToken);
                var permsAvant = existingPerms
                    .Where(p => p.PermissionCode != PermissionMarkers.EmptyPermissionsMarker)
                    .Select(p => p.PermissionCode)
                    .ToList();
                if (existingPerms.Count > 0)
                {
                    permRepo.RemoveRange(existingPerms);
                    await _rightsChangeJournal.EcrireAuditDroitAsync(
                        currentUserId, caller.Login ?? string.Empty,
                        "USER", entity.IdResponsable, entity.NomResponsable,
                        "RESET_USER_PERMS",
                        permsAvant, null,
                        cancellationToken);
                }
            }
        }

        return await ExecuteSaveUserInternalAsync(entity, dto, cancellationToken);
    }

    /// <summary>Réservé aux tests (internal, sans garde) : le code de production doit transmettre l'identité de l'appelant.</summary>
    internal async Task<UserDto> SaveUserAsync(UserDto dto, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<Responsable>();
        Responsable? entity = null;
        if (dto.Id != 0)
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
            if (entity.IdRole == AdminRules.AdminRoleId && dto.RoleId != AdminRules.AdminRoleId)
            {
                var otherAdmins = await repo.ListAsync(u => u.IdRole == AdminRules.AdminRoleId && u.IdResponsable != entity.IdResponsable, cancellationToken);
                if (otherAdmins.Count == 0)
                    throw new DomainException("Impossible de rétrograder le dernier administrateur.");
            }
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

    private async Task<UserDto> ExecuteSaveUserInternalAsync(Responsable? entity, UserDto dto, CancellationToken cancellationToken)
    {
        var repo = _uow.Repository<Responsable>();
        var (nom, login) = ValidateUserFields(dto);
        var loginModifie = false;
        var motDePasseModifie = false;
        if (dto.Id == 0)
        {
            await EnsureLoginAvailableAsync(repo, login, dto.Id, cancellationToken);
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new FieldValidationException("password", "Le mot de passe est obligatoire pour un nouvel utilisateur.");
            EnsurePasswordPolicy(dto.Password);

            entity = new Responsable
            {
                IdRole = dto.RoleId,
                NomResponsable = nom,
                Login = login,
                Password = _passwordHasher.HashPassword(null!, dto.Password),
                Matricule = dto.Matricule,
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim()
            };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity ??= await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
            loginModifie = !string.Equals((entity.Login ?? string.Empty).Trim(), login, StringComparison.Ordinal);
            // Ne pas bloquer la modification d'un compte historique dont le login serait déjà dupliqué :
            // l'unicité n'est vérifiée que lorsqu'un nouveau login est choisi.
            if (!string.Equals((entity.Login ?? string.Empty).Trim(), login, StringComparison.OrdinalIgnoreCase))
                await EnsureLoginAvailableAsync(repo, login, dto.Id, cancellationToken);

            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                var isSame = false;
                var rehashNeeded = false;
                if (!string.IsNullOrEmpty(entity.Password))
                {
                    try
                    {
                        var res = _passwordHasher.VerifyHashedPassword(entity, entity.Password, dto.Password);
                        isSame = res != PasswordVerificationResult.Failed;
                        rehashNeeded = res == PasswordVerificationResult.SuccessRehashNeeded;
                    }
                    catch (FormatException)
                    {
                        // Valeur historique en clair : comparaison à temps constant.
                        isSame = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(entity.Password), Encoding.UTF8.GetBytes(dto.Password));
                    }
                }

                if (!isSame)
                {
                    EnsurePasswordPolicy(dto.Password);
                    motDePasseModifie = true;
                    entity.Password = _passwordHasher.HashPassword(entity, dto.Password);
                    // Nouveau mot de passe : le compte est déverrouillé et les sessions ouvertes avec l'ancien sont fermées.
                    entity.TentativesEchouees = 0;
                    entity.VerrouilleJusqua = null;
                    await _refreshTokens.RevokeAllAsync(entity.IdResponsable, cancellationToken);
                }
                else if (rehashNeeded || PasswordHashClassifier.IsPlainText(entity.Password))
                {
                    // Même mot de passe stocké en clair ou dans un ancien format : hachage au format courant.
                    entity.Password = _passwordHasher.HashPassword(entity, dto.Password);
                }
            }

            entity.IdRole = dto.RoleId;
            entity.NomResponsable = nom;
            entity.Login = login;
            entity.Matricule = dto.Matricule;
            entity.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(entity.IdResponsable);

        // Notification best-effort : le service journalise et n'échoue jamais la requête.
        if (dto.Id == 0)
            await _email.SendUserCredentialsAsync(entity.Email, entity.Login, dto.Password, entity.NomResponsable, null, cancellationToken);
        else if (loginModifie || motDePasseModifie)
            await _email.SendUserUpdateAsync(entity.Email, entity.Login, motDePasseModifie ? dto.Password : null, entity.NomResponsable, null, loginModifie, motDePasseModifie, cancellationToken);

        return new UserDto(entity.IdResponsable, entity.IdRole, entity.NomResponsable, entity.Login ?? string.Empty, entity.Matricule, null) { Email = entity.Email };
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
