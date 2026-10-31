using AutoMapper;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Domain;

namespace SoftTime.Application.Services;

public class AuthService
{
    /// <summary>Rôle d'administration attendu lors du démarrage initial (voir le script de base saine).</summary>
    public const string RoleAdministrateur = "Administrateur";
    private const string EmptyPermissionsMarker = "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__";
    private const string EmptyRolePermissionsMarker = "__SOFTTIME_EMPTY_ROLE_PERMISSIONS__";

    private static readonly HashSet<string> KnownStaticPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "PARAMS:USERS", "PARAMS:USERS:CREATE", "PARAMS:USERS:UPDATE", "PARAMS:USERS:DELETE",
        "PARAMS:ROLES", "PARAMS:ROLES:CREATE", "PARAMS:ROLES:UPDATE", "PARAMS:ROLES:DELETE",
        "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS", "PARAMS:CLOCK_PARAMS", "PARAMS:CORRESPONDENCE",
        "PARAMS:CARDPAIE", "PARAMS:CARDPAIE:CREATE", "PARAMS:CARDPAIE:UPDATE", "PARAMS:CARDPAIE:DELETE",
        "PARAMS:CATEGORIES", "PARAMS:CATEGORIES:CREATE", "PARAMS:CATEGORIES:UPDATE", "PARAMS:CATEGORIES:DELETE",
        "PARAMS:AFFECTATIONS", "PARAMS:AFFECTATIONS:CREATE", "PARAMS:AFFECTATIONS:UPDATE", "PARAMS:AFFECTATIONS:DELETE",
        "PARAMS:TOLERANCES", "PARAMS:TOLERANCES:CREATE", "PARAMS:TOLERANCES:UPDATE",
        "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:CREATE", "PARAMS:HOLIDAYS:UPDATE", "PARAMS:HOLIDAYS:DELETE",
        "PARAMS:MAJORATIONS", "PARAMS:MAJORATIONS:UPDATE",
        "PARAMS:CODE_CONSTANTES",
        "PARAMS:ABSENCE_CODES", "PARAMS:ABSENCE_CODES:CREATE", "PARAMS:ABSENCE_CODES:UPDATE",
        "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE", "PROC:SHIFTS:DELETE",
        "PROC:EMPLOYEE_PLANNING", "PROC:EMPLOYEE_PLANNING:CREATE", "PROC:EMPLOYEE_PLANNING:UPDATE", "PROC:EMPLOYEE_PLANNING:DELETE",
        "PROC:PUNCHES",
        "PROC:ANOMALIES", "PROC:ANOMALIES:DELETE",
        "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
        "PROC:WEEKLY_VALIDATION", "PROC:OVERTIME",
        "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES",
        "TRACE:AUDIT",
        "REPORTS:REPORTS"
    };

    private static bool IsKnownPermissionCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (code.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase))
            return true;
        return KnownStaticPermissions.Contains(code);
    }

    private static HashSet<decimal> DeduirePortes(IEnumerable<string> permCodes)
        => PermissionCatalog.DeduirePortes(permCodes);

    private static List<string> BuildDefaultPermissionsList(
        HashSet<int> rights,
        IEnumerable<T_BDD_SAGE> sageDbs,
        IEnumerable<T_BDD_POINTEUSE> pointeuseDbs)
        => PermissionCatalog.BuildDefaultPermissionsList(rights, sageDbs, pointeuseDbs);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    private async Task EcrireAuditDroitAsync(
        decimal idOperateur, string loginOperateur,
        string typeCible, decimal idCible, string? nomCible,
        string action,
        IEnumerable<string>? permsAvant, IEnumerable<string>? permsApres,
        CancellationToken cancellationToken)
    {
        var avant = permsAvant == null ? null : string.Join(",", permsAvant);
        var apres = permsApres == null ? null : string.Join(",", permsApres);
        await _uow.Repository<T_AUDIT_DROIT>().AddAsync(new T_AUDIT_DROIT
        {
            DATE_ACTION = DateTime.UtcNow,
            ID_OPERATEUR = idOperateur,
            LOGIN_OPERATEUR = Truncate(loginOperateur, 100) ?? string.Empty,
            TYPE_CIBLE = Truncate(typeCible, 20) ?? string.Empty,
            ID_CIBLE = idCible,
            NOM_CIBLE = Truncate(nomCible, 100),
            ACTION = Truncate(action, 50) ?? string.Empty,
            PERMS_AVANT = avant,
            PERMS_APRES = apres
        }, cancellationToken);
    }

    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;
    private readonly IEmailService _email;
    private readonly IPasswordHasher<T_RESPONSABLE> _passwordHasher;
    // Appelant HTTP, obligatoire : aucune écriture sur un autre compte sans contrôle d'administrateur.
    private readonly ICurrentUser _currentUser;
    private readonly EffectiveRightsService _effectiveRights;

    public const string ReadDenied = "Vous n'avez pas la permission requise pour cette consultation.";

    // Lectures sensibles de la section Paramètres : permission fine requise (l'une ou l'autre quand plusieurs écrans partagent la route).
    public static readonly string[] ReadUsers = { "PARAMS:USERS" };
    public static readonly string[] ReadUserList = { "PARAMS:USERS", "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS" };
    public static readonly string[] ReadRoles = { "PARAMS:ROLES" };
    public static readonly string[] ReadRoleList = { "PARAMS:USERS", "PARAMS:PRIVILEGES" };
    public static readonly string[] ReadPrivileges = { "PARAMS:PRIVILEGES" };
    public static readonly string[] ReadPrivilegeList = { "PARAMS:PRIVILEGES", "PARAMS:ROLES" };
    public static readonly string[] ReadDbAccess = { "PARAMS:DB_ACCESS" };

    /// <summary>
    /// Consultation sensible : administrateur, ou porte 2 ET l'une des permissions fines, lues dans les droits effectifs
    /// (pas dans le jeton). Échec fermé si la lecture des droits échoue.
    /// </summary>
    public async Task EnsureCanReadAsync(string[] anyOf, CancellationToken cancellationToken = default)
    {
        if (AdminAccess.IsAdmin(_currentUser)) return;
        if (_currentUser.UserId is not decimal uid)
            throw new ForbiddenOperationException(ReadDenied);
        EffectiveRights rights;
        try
        {
            rights = await _effectiveRights.GetAsync(uid, ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _effectiveRights.LogReadFailure(uid, ex);
            throw new ForbiddenOperationException(ReadDenied);
        }
        if (!rights.HasGate(Rights.Parameters) || !anyOf.Any(rights.Has))
            throw new ForbiddenOperationException(ReadDenied);
    }

    /// <summary>Opérations sur les autres comptes : réservées aux administrateurs (rôle 1) (rôle 1 uniquement).</summary>
    private void RequireAdministrator()
    {
        AdminAccess.Require(_currentUser);
    }

    public AuthService(
        IUnitOfWork uow,
        ITokenService tokens,
        IMapper mapper,
        IEmailService email,
        ICurrentUser currentUser,
        EffectiveRightsService effectiveRights,
        IPasswordHasher<T_RESPONSABLE>? passwordHasher = null)
    {
        _uow = uow;
        _tokens = tokens;
        _mapper = mapper;
        _email = email;
        _passwordHasher = passwordHasher ?? new PasswordHasher<T_RESPONSABLE>();
        _currentUser = currentUser;
        _effectiveRights = effectiveRights;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var login = (request.Login ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
            throw new UnauthorizedAccessException("Identifiants invalides.");

        var users = await _uow.Repository<T_RESPONSABLE>().ListAsync(
            u => u.LOGIN == login, cancellationToken);
        if (users.Count == 0)
        {
            users = (await _uow.Repository<T_RESPONSABLE>().ListAsync(_ => true, cancellationToken))
                .Where(u => string.Equals((u.LOGIN ?? string.Empty).Trim(), login, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        var user = users.FirstOrDefault() ?? throw new UnauthorizedAccessException("Identifiants invalides.");

        var passwordVerified = false;
        var needsUpgrade = false;

        if (!string.IsNullOrEmpty(user.PASSWORD))
        {
            try
            {
                var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PASSWORD, password);
                if (verifyResult == PasswordVerificationResult.Success)
                {
                    passwordVerified = true;
                }
                else if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    passwordVerified = true;
                    needsUpgrade = true;
                }
            }
            catch
            {
                // FormatException if stored string is not valid Base64
            }

            if (!passwordVerified && PasswordSecurityHelper.IsPlainText(user.PASSWORD) && string.Equals(user.PASSWORD, password, StringComparison.Ordinal))
            {
                passwordVerified = true;
                needsUpgrade = true;
            }
        }

        if (!passwordVerified)
            throw new UnauthorizedAccessException("Identifiants invalides.");

        if (needsUpgrade)
        {
            user.PASSWORD = _passwordHasher.HashPassword(user, password);
            _uow.Repository<T_RESPONSABLE>().Update(user);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        var (rights, effective) = await TokenGatesAsync(user, cancellationToken);
        var token = _tokens.CreateToken(user.IDRESPONSABLE, user.LOGIN ?? login, user.IDROLE, user.MATRICULE, rights);
        var detailed = await GetUserDetailedPermissionsAsync(user.IDRESPONSABLE, cancellationToken);
        var authorizedSages = await UsableSageDatabasesAsync(user, effective, cancellationToken);
        return new LoginResponse(token, user.IDRESPONSABLE, user.LOGIN ?? login, user.NOMRESPONSABLE ?? login, user.IDROLE, user.MATRICULE, rights, authorizedSages.Select(_mapper.Map<SageDbDto>).ToList(), detailed.GrantedPermissions, detailed.IsCustomized);
    }

    /// <summary>
    /// Portes du claim « rights » : celles des droits effectifs du compte (rôle + ajouts − retraits), lues à la connexion.
    /// L'administrateur (rôle 1) a les portes 1 à 6. Un changement de droits ne s'applique qu'à la reconnexion.
    /// </summary>
    private async Task<(List<int> Gates, EffectiveRights? Effective)> TokenGatesAsync(T_RESPONSABLE user, CancellationToken cancellationToken)
    {
        if (user.IDROLE == AdminRules.AdminRoleId)
            return (new List<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports }, null);
        var effective = await _effectiveRights.GetAsync(user.IDRESPONSABLE, fresh: true, ct: cancellationToken);
        return (effective.Gates.OrderBy(g => g).ToList(), effective);
    }

    /// <summary>Bases SAGE proposées au compte : exactement celles qu'il peut utiliser (même règle que le contrôle serveur).</summary>
    private async Task<List<T_BDD_SAGE>> UsableSageDatabasesAsync(T_RESPONSABLE user, EffectiveRights? effective, CancellationToken cancellationToken)
    {
        var sages = await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, cancellationToken);
        if (effective is null)
            return sages;
        return sages.Where(s => effective.CanUseDatabase(DatabaseKind.Sage, s.NOM_BD)).ToList();
    }

    public async Task<LoginResponse> GetMeAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");
        var (rights, effective) = await TokenGatesAsync(user, cancellationToken);
        var detailed = await GetUserDetailedPermissionsAsync(user.IDRESPONSABLE, cancellationToken);
        var authorizedSages = await UsableSageDatabasesAsync(user, effective, cancellationToken);
        return new LoginResponse(string.Empty, user.IDRESPONSABLE, user.LOGIN ?? string.Empty, user.NOMRESPONSABLE ?? string.Empty, user.IDROLE, user.MATRICULE, rights, authorizedSages.Select(_mapper.Map<SageDbDto>).ToList(), detailed.GrantedPermissions, detailed.IsCustomized);
    }

    public async Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadUsers, cancellationToken);
        var u = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        return new UserDto(u.IDRESPONSABLE, u.IDROLE, u.NOMRESPONSABLE ?? string.Empty, u.LOGIN ?? string.Empty, u.MATRICULE, null) { Email = u.EMAIL };
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadUserList, cancellationToken);
        var list = await _uow.Repository<T_RESPONSABLE>().ListAsync(_ => true, cancellationToken);
        return list
            .OrderBy(u => u.NOMRESPONSABLE)
            .ThenBy(u => u.LOGIN)
            .Select(u => new UserDto(u.IDRESPONSABLE, u.IDROLE, u.NOMRESPONSABLE ?? string.Empty, u.LOGIN ?? string.Empty, u.MATRICULE, null) { Email = u.EMAIL })
            .ToList();
    }

    public async Task<PagedResult<UserDto>> GetUsersPagedAsync(
        int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadUsers, cancellationToken);
        return await PagedLists.PageAsync(
            _uow.Repository<T_RESPONSABLE>(),
            _ => true,
            q => q.OrderBy(u => u.NOMRESPONSABLE).ThenBy(u => u.LOGIN).ThenBy(u => u.IDRESPONSABLE),
            page,
            pageSize,
            u => new UserDto(u.IDRESPONSABLE, u.IDROLE, u.NOMRESPONSABLE ?? string.Empty, u.LOGIN ?? string.Empty, u.MATRICULE, null) { Email = u.EMAIL },
            cancellationToken);
    }

    /// <summary>
    /// Installation vierge ? Portage de <c>is-first-run</c> du desktop, avec un critère plus sain :
    /// le desktop cherchait un rôle nommé « administrateur », ici on constate simplement qu'aucun
    /// compte n'existe. Le critère ne dépend donc pas d'une convention de nommage des rôles, et il
    /// reste vrai après la suppression du dernier compte (seul moyen de sortir d'un verrouillage total).
    /// </summary>
    public async Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default)
        => (await _uow.Repository<T_RESPONSABLE>().ListAsync(_ => true, cancellationToken)).Count == 0;

    /// <summary>
    /// Crée le premier compte administrateur. Refusé dès qu'un compte existe, et le rôle
    /// <see cref="RoleAdministrateur"/> se voit garantir tous les droits : sans cela, l'application
    /// serait inaccessible juste après son installation.
    /// </summary>
    public async Task<AdminSetupResultDto> CreateAdminAsync(AdminSetupDto dto, CancellationToken cancellationToken = default)
    {
        if (!await IsFirstRunAsync(cancellationToken))
            throw new InvalidOperationException(
                "Des comptes existent déjà : la création initiale n'est plus possible. Utilisez l'écran Utilisateurs.");
        var login = (dto.Login ?? string.Empty).Trim();
        var motDePasse = dto.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dto.Nom))
            throw new InvalidOperationException("Le nom est requis.");
        if (string.IsNullOrWhiteSpace(login))
            throw new InvalidOperationException("L'identifiant est requis.");
        if (string.IsNullOrWhiteSpace(motDePasse))
            throw new InvalidOperationException("Le mot de passe est requis.");
        PasswordPolicy.EnsureValid(motDePasse);

        // Le premier compte reçoit toujours le rôle d'identifiant 1 : seul ce rôle est administrateur.
        var role = await _uow.Repository<T_ROLE>().GetByIdAsync(AdminRules.AdminRoleId, cancellationToken)
            ?? throw new InvalidOperationException(
                "Le rôle Administrateur (identifiant 1) est introuvable : exécutez le script de création de la base.");

        var entity = new T_RESPONSABLE
        {
            IDROLE = role.IDROLE,
            NOMRESPONSABLE = dto.Nom.Trim(),
            LOGIN = login,
            PASSWORD = _passwordHasher.HashPassword(null!, motDePasse),
            EMAIL = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim()
        };
        await _uow.Repository<T_RESPONSABLE>().AddAsync(entity, cancellationToken);
        await EnsureRoleHasAllRightsAsync(role.IDROLE, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var envoye = await _email.SendUserCredentialsAsync(entity.EMAIL, entity.LOGIN, motDePasse, entity.NOMRESPONSABLE, null, cancellationToken);
        var message = envoye
            ? "Compte administrateur créé et identifiants envoyés par e-mail."
            : _email.EstConfigure
                ? "Compte administrateur créé. L'envoi de l'e-mail a échoué : communiquez les identifiants manuellement."
                : "Compte administrateur créé. Aucune configuration SMTP (« Email ») : communiquez les identifiants manuellement.";
        return new AdminSetupResultDto(entity.IDRESPONSABLE, entity.LOGIN ?? login, envoye, message);
    }

    /// <summary>Garantit que le rôle d'administration détient tous les droits (idempotent).</summary>
    private async Task EnsureRoleHasAllRightsAsync(decimal roleId, CancellationToken cancellationToken)
    {
        var droits = await _uow.Repository<T_DROIT>().ListAsync(_ => true, cancellationToken);
        var existants = await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == roleId, cancellationToken);
        var manquants = droits.Where(d => existants.All(e => e.IDDROIT != d.IDDROIT)).ToList();
        if (manquants.Count == 0)
            return;
        var repo = _uow.Repository<T_DROIT_ROLE>();
        foreach (var droit in manquants)
            await repo.AddAsync(new T_DROIT_ROLE { IDROLE = roleId, IDDROIT = droit.IDDROIT }, cancellationToken);
    }

    public async Task<UserDto> SaveUserAsync(decimal currentUserId, UserDto dto, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var repo = _uow.Repository<T_RESPONSABLE>();
        var caller = await repo.GetByIdAsync(currentUserId, cancellationToken);
        if (caller == null)
            throw new InvalidOperationException("Utilisateur appelant introuvable.");

        var callerRole = await _uow.Repository<T_ROLE>().GetByIdAsync(caller.IDROLE, cancellationToken);
        if (callerRole == null)
            throw new InvalidOperationException("Utilisateur appelant introuvable.");

        var callerIsSuperAdmin = AdminRules.IsSuperAdmin(caller.IDROLE);


        T_RESPONSABLE? entity = null;
        if (dto.Id == 0)
        {
            if (dto.RoleId == AdminRules.AdminRoleId)
            {
                if (!callerIsSuperAdmin)
                    throw new InvalidOperationException("Seul un super-administrateur peut attribuer le rôle Administrateur.");
            }
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new KeyNotFoundException();

            if (dto.Id == currentUserId && entity.IDROLE != dto.RoleId)
                throw new InvalidOperationException("Vous ne pouvez pas modifier votre propre rôle.");


            if (entity.IDROLE == AdminRules.AdminRoleId)
            {
                if (!callerIsSuperAdmin)
                    throw new InvalidOperationException("Seul un super-administrateur peut modifier un compte administrateur.");
            }

            if (dto.RoleId == AdminRules.AdminRoleId)
            {
                if (!callerIsSuperAdmin)
                    throw new InvalidOperationException("Seul un super-administrateur peut attribuer le rôle Administrateur.");
            }

            if (entity.IDROLE == AdminRules.AdminRoleId && dto.RoleId != AdminRules.AdminRoleId)
            {
                var otherAdmins = await repo.ListAsync(u => u.IDROLE == AdminRules.AdminRoleId && u.IDRESPONSABLE != entity.IDRESPONSABLE, cancellationToken);
                if (otherAdmins.Count == 0)
                    throw new InvalidOperationException("Impossible de rétrograder le dernier administrateur.");
            }

            if (entity.IDROLE != dto.RoleId && dto.ResetPermissions)
            {
                var permRepo = _uow.Repository<T_USER_PERMISSION>();
                var existingPerms = await permRepo.ListAsync(p => p.IDRESPONSABLE == entity.IDRESPONSABLE, cancellationToken);
                var permsAvant = existingPerms
                    .Where(p => p.PERMISSION_CODE != EmptyPermissionsMarker)
                    .Select(p => p.PERMISSION_CODE)
                    .ToList();
                permRepo.RemoveRange(existingPerms);
                await EcrireAuditDroitAsync(
                    currentUserId, caller.LOGIN ?? string.Empty,
                    "USER", entity.IDRESPONSABLE, entity.NOMRESPONSABLE,
                    "RESET_USER_PERMS",
                    permsAvant, null,
                    cancellationToken);
            }
        }

        return await ExecuteSaveUserInternalAsync(entity, dto, cancellationToken);
    }

    /// <summary>Réservé aux tests (internal, sans garde) : le code de production doit transmettre l'identité de l'appelant.</summary>
    internal async Task<UserDto> SaveUserAsync(UserDto dto, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_RESPONSABLE>();
        T_RESPONSABLE? entity = null;
        if (dto.Id != 0)
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new KeyNotFoundException();
            if (entity.IDROLE == AdminRules.AdminRoleId && dto.RoleId != AdminRules.AdminRoleId)
            {
                var otherAdmins = await repo.ListAsync(u => u.IDROLE == AdminRules.AdminRoleId && u.IDRESPONSABLE != entity.IDRESPONSABLE, cancellationToken);
                if (otherAdmins.Count == 0)
                    throw new InvalidOperationException("Impossible de rétrograder le dernier administrateur.");
            }
        }
        return await ExecuteSaveUserInternalAsync(entity, dto, cancellationToken);
    }

    private async Task<UserDto> ExecuteSaveUserInternalAsync(T_RESPONSABLE? entity, UserDto dto, CancellationToken cancellationToken)
    {
        var repo = _uow.Repository<T_RESPONSABLE>();
        var loginModifie = false;
        var motDePasseModifie = false;
        if (dto.Id == 0)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new InvalidOperationException("Le mot de passe est requis pour un nouvel utilisateur.");
            PasswordPolicy.EnsureValid(dto.Password);

            entity = new T_RESPONSABLE
            {
                IDROLE = dto.RoleId,
                NOMRESPONSABLE = dto.Nom,
                LOGIN = dto.Login,
                PASSWORD = _passwordHasher.HashPassword(null!, dto.Password),
                MATRICULE = dto.Matricule,
                EMAIL = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim()
            };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity ??= await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new KeyNotFoundException();
            loginModifie = !string.Equals((entity.LOGIN ?? string.Empty).Trim(), (dto.Login ?? string.Empty).Trim(), StringComparison.Ordinal);

            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                var isSame = false;
                if (!string.IsNullOrEmpty(entity.PASSWORD))
                {
                    try
                    {
                        var res = _passwordHasher.VerifyHashedPassword(entity, entity.PASSWORD, dto.Password);
                        if (res == PasswordVerificationResult.Success)
                            isSame = true;
                    }
                    catch
                    {
                        if (string.Equals(entity.PASSWORD, dto.Password, StringComparison.Ordinal))
                            isSame = true;
                    }
                }

                if (!isSame)
                {
                    PasswordPolicy.EnsureValid(dto.Password);
                    motDePasseModifie = true;
                    entity.PASSWORD = _passwordHasher.HashPassword(entity, dto.Password);
                }
                else if (!PasswordSecurityHelper.IsHashedPassword(entity.PASSWORD))
                {
                    // Plain-text legacy password matching the same password: upgrade to hash!
                    entity.PASSWORD = _passwordHasher.HashPassword(entity, dto.Password);
                }
            }

            entity.IDROLE = dto.RoleId;
            entity.NOMRESPONSABLE = dto.Nom;
            entity.LOGIN = dto.Login;
            entity.MATRICULE = dto.Matricule;
            entity.EMAIL = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(entity.IDRESPONSABLE);

        // Notification best-effort : le service journalise et n'échoue jamais la requête.
        if (dto.Id == 0)
            await _email.SendUserCredentialsAsync(entity.EMAIL, entity.LOGIN, dto.Password, entity.NOMRESPONSABLE, null, cancellationToken);
        else if (loginModifie || motDePasseModifie)
            await _email.SendUserUpdateAsync(entity.EMAIL, entity.LOGIN, motDePasseModifie ? dto.Password : null, entity.NOMRESPONSABLE, null, loginModifie, motDePasseModifie, cancellationToken);

        return new UserDto(entity.IDRESPONSABLE, entity.IDROLE, entity.NOMRESPONSABLE, entity.LOGIN ?? string.Empty, entity.MATRICULE, null) { Email = entity.EMAIL };
    }

    public async Task DeleteUserAsync(decimal currentUserId, decimal id, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var repo = _uow.Repository<T_RESPONSABLE>();
        var caller = await repo.GetByIdAsync(currentUserId, cancellationToken);
        if (caller == null)
            throw new InvalidOperationException("Utilisateur appelant introuvable.");

        var callerRole = await _uow.Repository<T_ROLE>().GetByIdAsync(caller.IDROLE, cancellationToken);
        if (callerRole == null)
            throw new InvalidOperationException("Utilisateur appelant introuvable.");

        var callerIsSuperAdmin = AdminRules.IsSuperAdmin(caller.IDROLE);

        if (currentUserId == id)
            throw new InvalidOperationException("Vous ne pouvez pas supprimer votre propre compte.");

        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();

        if (entity.IDROLE == AdminRules.AdminRoleId)
        {
            if (!callerIsSuperAdmin)
                throw new InvalidOperationException("Seul un super-administrateur peut supprimer un compte administrateur.");
        }

        if (entity.IDROLE == AdminRules.AdminRoleId)
        {
            var otherAdmins = await repo.ListAsync(u => u.IDROLE == AdminRules.AdminRoleId && u.IDRESPONSABLE != entity.IDRESPONSABLE, cancellationToken);
            if (otherAdmins.Count == 0)
                throw new InvalidOperationException("Impossible de supprimer le dernier administrateur.");
        }

        await RemoveAccountRightsAsync(entity.IDRESPONSABLE, cancellationToken);
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
        var permissions = _uow.Repository<T_USER_PERMISSION>();
        permissions.RemoveRange(await permissions.ListAsync(p => p.IDRESPONSABLE == userId, cancellationToken));
        var access = _uow.Repository<T_BDD_AUTORISE>();
        access.RemoveRange(await access.ListAsync(a => a.IDRESPONSABLE == userId, cancellationToken));
    }

    /// <summary>Réservé aux tests (internal, sans garde) : le code de production doit transmettre l'identité de l'appelant.</summary>
    internal async Task DeleteUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_RESPONSABLE>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        if (entity.IDROLE == AdminRules.AdminRoleId)
        {
            var otherAdmins = await repo.ListAsync(u => u.IDROLE == AdminRules.AdminRoleId && u.IDRESPONSABLE != entity.IDRESPONSABLE, cancellationToken);
            if (otherAdmins.Count == 0)
                throw new InvalidOperationException("Impossible de supprimer le dernier administrateur.");
        }
        await RemoveAccountRightsAsync(entity.IDRESPONSABLE, cancellationToken);
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(id);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadRoleList, cancellationToken);
        return (await _uow.Repository<T_ROLE>().ListAsync(_ => true, cancellationToken))
            .Select(r => new RoleDto(r.IDROLE, r.NOMROLE)).ToList();
    }

    public async Task<PagedResult<RoleDto>> GetRolesPagedAsync(
        int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadRoles, cancellationToken);
        return await PagedLists.PageAsync(
            _uow.Repository<T_ROLE>(),
            _ => true,
            q => q.OrderBy(r => r.IDROLE),
            page,
            pageSize,
            r => new RoleDto(r.IDROLE, r.NOMROLE),
            cancellationToken);
    }

    public async Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id == AdminRules.AdminRoleId)
            throw new InvalidOperationException("Le rôle Administrateur ne peut pas être modifié ni renommé.");
        var repo = _uow.Repository<T_ROLE>();
        T_ROLE entity;
        if (dto.Id == 0)
        {
            entity = new T_ROLE { NOMROLE = dto.Nom };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new KeyNotFoundException();
            entity.NOMROLE = dto.Nom;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
        return new RoleDto(entity.IDROLE, entity.NOMROLE);
    }

    public async Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default)
    {
        if (id == AdminRules.AdminRoleId)
            throw new InvalidOperationException("Le rôle Administrateur ne peut pas être supprimé.");
        var repo = _uow.Repository<T_ROLE>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();

        // C5 — Nettoyage à la suppression d'un rôle
        var rpRepo = _uow.Repository<T_ROLE_PERMISSION>();
        var rolePerms = await rpRepo.ListAsync(p => p.IDROLE == id, cancellationToken);
        rpRepo.RemoveRange(rolePerms);

        var droitRepo = _uow.Repository<T_DROIT_ROLE>();
        var droits = await droitRepo.ListAsync(d => d.IDROLE == id, cancellationToken);
        droitRepo.RemoveRange(droits);

        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    public async Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPrivilegeList, cancellationToken);
        return (await _uow.Repository<T_DROIT>().ListAsync(_ => true, cancellationToken))
            .Select(d => new DroitDto(d.IDDROIT, d.DROIT)).ToList();
    }

    public async Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPrivilegeList, cancellationToken);
        var list = await _uow.Repository<T_DROIT_ROLE>().ListAsync(
            p => !roleId.HasValue || p.IDROLE == roleId.Value, cancellationToken);
        return list.Select(p => new PrivilegeDto(p.IDDROITROLE, p.IDROLE, p.IDDROIT)).ToList();
    }

    public async Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default)
    {
        if (roleId == AdminRules.AdminRoleId)
            throw new InvalidOperationException("Les privilèges du rôle Administrateur ne sont pas modifiables.");
        // Les portes d'un rôle ayant des droits détaillés se déduisent de ces droits (SaveRoleDetailedPermissionsAsync) : écrire
        // T_DROIT_ROLE à la main n'aurait aucun effet sur le jeton et le contredirait.
        if ((await _uow.Repository<T_ROLE_PERMISSION>().ListAsync(p => p.IDROLE == roleId, cancellationToken)).Count > 0)
            throw new InvalidOperationException("Ce rôle a des droits détaillés : modifiez-les depuis la page Privilèges, les portes en sont déduites.");
        var repo = _uow.Repository<T_DROIT_ROLE>();
        var existing = await repo.ListAsync(p => p.IDROLE == roleId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var d in droitIds.Distinct())
            await repo.AddAsync(new T_DROIT_ROLE { IDROLE = roleId, IDDROIT = d }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    public async Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadDbAccess, cancellationToken);
        var list = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(
            a => !userId.HasValue || a.IDRESPONSABLE == userId.Value, cancellationToken);
        return list.Select(a => new DbAccessDto(a.IDBDDAUTORISE, a.IDRESPONSABLE, a.IDBDD)).ToList();
    }

    public async Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var repo = _uow.Repository<T_BDD_AUTORISE>();
        var existing = await repo.ListAsync(a => a.IDRESPONSABLE == userId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var id in sageDbIds.Distinct())
            await repo.AddAsync(new T_BDD_AUTORISE { IDRESPONSABLE = userId, IDBDD = id }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(userId);
    }

    public async Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        var customList = await _uow.Repository<T_USER_PERMISSION>().ListAsync(
            p => p.IDRESPONSABLE == userId && p.IS_GRANTED, cancellationToken);

        var rolePrivileges = await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == user.IDROLE, cancellationToken);
        var rights = rolePrivileges.Select(p => (int)p.IDDROIT).ToHashSet();
        var isAdmin = user.IDROLE == 1;
        if (isAdmin)
        {
            rights = new HashSet<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        }

        var access = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == userId, cancellationToken);
        var sageIds = access.Select(a => a.IDBDD).ToHashSet();
        // Admin : toutes les bases ; autres : seulement celles de T_BDD_AUTORISE.
        var sageDbs = (isAdmin || rights.Contains(Rights.Databases))
            ? await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, cancellationToken)
            : await _uow.Repository<T_BDD_SAGE>().ListAsync(s => sageIds.Contains(s.ID), cancellationToken);
        var pointeuseDbs = await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, cancellationToken);

        var defaults = BuildDefaultPermissionsList(rights, sageDbs, pointeuseDbs);

        if (customList.Count > 0)
        {
            var perms = customList
                .Where(p => p.PERMISSION_CODE != EmptyPermissionsMarker)
                .Select(p => p.PERMISSION_CODE)
                .ToList();

            if (isAdmin)
            {
                // Un administrateur doit impérativement avoir tous les privilèges système
                // même si une personnalisation partielle a été enregistrée.
                perms.AddRange(defaults);
            }
            return new UserDetailedPermissionsDto(userId, perms.Distinct().ToList(), true);
        }

        // B2 — Droits effectifs : droits du rôle si configurés
        var roleCustomPerms = await _uow.Repository<T_ROLE_PERMISSION>().ListAsync(
            p => p.IDROLE == user.IDROLE && p.IS_GRANTED, cancellationToken);
        if (roleCustomPerms.Count > 0)
        {
            var rolePerms = roleCustomPerms
                .Where(p => p.PERMISSION_CODE != EmptyRolePermissionsMarker)
                .Select(p => p.PERMISSION_CODE)
                .Distinct()
                .ToList();
            if (isAdmin)
            {
                rolePerms.AddRange(defaults);
                rolePerms = rolePerms.Distinct().ToList();
            }
            return new UserDetailedPermissionsDto(userId, rolePerms, false);
        }

        return new UserDetailedPermissionsDto(userId, defaults.Distinct().ToList(), false);
    }

    public async Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var caller = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Utilisateur connecté introuvable.");

        var target = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur cible introuvable.");

        var callerIsAdmin = AdminRules.IsSuperAdmin(caller.IDROLE);

        if (currentUserId == targetUserId && !callerIsAdmin)
        {
            throw new InvalidOperationException("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.");
        }

        var targetIsAdmin = target.IDROLE == 1;

        var toSaveList = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p) && p != EmptyPermissionsMarker)
            .ToList();

        if (targetIsAdmin)
        {
            // Un administrateur doit obligatoirement conserver l'accès à toutes les bases et aux écrans clés
            toSaveList.Add("SAGE:CREATE");
            toSaveList.Add("POINTEUSE:CREATE");
            toSaveList.Add("PARAMS:PRIVILEGES");
            toSaveList.Add("PARAMS:ROLES");
            toSaveList.Add("PARAMS:USERS");
            toSaveList.Add("PARAMS:DB_ACCESS");

            var allSages = await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, cancellationToken);
            foreach (var s in allSages)
            {
                var n = s.NOM_BD ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n)) continue;
                toSaveList.Add($"SAGE:{n}:READ");
                toSaveList.Add($"SAGE:{n}:UPDATE");
                toSaveList.Add($"SAGE:{n}:DELETE");
            }

            var allPointeuses = await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, cancellationToken);
            foreach (var p in allPointeuses)
            {
                var n = p.NOM_BD ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n)) continue;
                toSaveList.Add($"POINTEUSE:{n}:READ");
                toSaveList.Add($"POINTEUSE:{n}:UPDATE");
                toSaveList.Add($"POINTEUSE:{n}:DELETE");
            }
        }

        var repo = _uow.Repository<T_USER_PERMISSION>();
        var existing = await repo.ListAsync(p => p.IDRESPONSABLE == targetUserId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PERMISSION_CODE != EmptyPermissionsMarker)
            .Select(p => p.PERMISSION_CODE)
            .ToList();
        repo.RemoveRange(existing);

        foreach (var p in toSaveList.Distinct())
        {
            await repo.AddAsync(new T_USER_PERMISSION
            {
                IDRESPONSABLE = targetUserId,
                PERMISSION_CODE = p.Trim(),
                IS_GRANTED = true
            }, cancellationToken);
        }

        // Une ligne sentinelle conserve l'intention "aucun privilège" : sans elle,
        // l'absence de ligne serait interprétée comme un retour aux droits du rôle.
        if (toSaveList.Count == 0)
        {
            await repo.AddAsync(new T_USER_PERMISSION
            {
                IDRESPONSABLE = targetUserId,
                PERMISSION_CODE = EmptyPermissionsMarker,
                IS_GRANTED = true
            }, cancellationToken);
        }

        await EcrireAuditDroitAsync(
            currentUserId, caller.LOGIN ?? string.Empty,
            "USER", targetUserId, target.NOMRESPONSABLE,
            "UPDATE_USER_PERMS",
            permsAvant, toSaveList,
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    private async Task<List<string>> ComputeDefaultPermissionsForRoleAsync(decimal roleId, CancellationToken cancellationToken)
    {
        var rolePrivileges = await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == roleId, cancellationToken);
        var rights = rolePrivileges.Select(p => (int)p.IDDROIT).ToHashSet();
        var isAdmin = roleId == AdminRules.AdminRoleId;
        if (isAdmin)
        {
            rights = new HashSet<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        }

        var sageDbs = (isAdmin || rights.Contains(Rights.Databases))
            ? await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, cancellationToken)
            : (IReadOnlyCollection<T_BDD_SAGE>)Array.Empty<T_BDD_SAGE>();
        var pointeuseDbs = (isAdmin || rights.Contains(Rights.Databases))
            ? await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, cancellationToken)
            : (IReadOnlyCollection<T_BDD_POINTEUSE>)Array.Empty<T_BDD_POINTEUSE>();

        return BuildDefaultPermissionsList(rights, sageDbs, pointeuseDbs);
    }

    public async Task<RoleDetailedPermissionsDto> GetRoleDetailedPermissionsAsync(decimal roleId, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPrivileges, cancellationToken);
        var roleList = await _uow.Repository<T_ROLE_PERMISSION>().ListAsync(
            p => p.IDROLE == roleId && p.IS_GRANTED, cancellationToken);

        if (roleList.Count == 0)
        {
            var defaultPerms = await ComputeDefaultPermissionsForRoleAsync(roleId, cancellationToken);
            return new RoleDetailedPermissionsDto(roleId, defaultPerms, false);
        }

        var perms = roleList
            .Where(p => p.PERMISSION_CODE != EmptyRolePermissionsMarker)
            .Select(p => p.PERMISSION_CODE)
            .Distinct()
            .ToList();

        return new RoleDetailedPermissionsDto(roleId, perms, true);
    }

    public async Task SaveRoleDetailedPermissionsAsync(decimal currentUserId, decimal targetRoleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        if (permissions == null)
            throw new InvalidOperationException("La liste des permissions ne peut pas être nulle.");

        var caller = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new InvalidOperationException("Utilisateur appelant introuvable.");

        var callerRole = await _uow.Repository<T_ROLE>().GetByIdAsync(caller.IDROLE, cancellationToken)
            ?? throw new InvalidOperationException("Utilisateur appelant introuvable.");

        if (!AdminRules.IsSuperAdmin(caller.IDROLE))
            throw new InvalidOperationException("Seul un administrateur peut modifier les permissions d'un rôle.");

        if (targetRoleId == AdminRules.AdminRoleId)
            throw new InvalidOperationException("Les privilèges du rôle Administrateur ne sont pas modifiables.");

        var targetRole = await _uow.Repository<T_ROLE>().GetByIdAsync(targetRoleId, cancellationToken)
            ?? throw new KeyNotFoundException("Rôle cible introuvable.");

        var normalizedPermissions = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Select(p =>
            {
                if (p.StartsWith("SAGE:", StringComparison.OrdinalIgnoreCase) ||
                    p.StartsWith("POINTEUSE:", StringComparison.OrdinalIgnoreCase))
                    return p;
                return p.ToUpperInvariant();
            })
            .Where(p => p != EmptyRolePermissionsMarker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tooLongCodes = normalizedPermissions
            .Where(p => p.Length > 128)
            .ToList();
        if (tooLongCodes.Count > 0)
            throw new InvalidOperationException($"Code de permission trop long (128 caractères maximum) : {string.Join(", ", tooLongCodes)}.");

        var invalidCodes = normalizedPermissions
            .Where(p => !IsKnownPermissionCode(p))
            .ToList();
        if (invalidCodes.Count > 0)
            throw new InvalidOperationException($"Codes de permission inconnus : {string.Join(", ", invalidCodes)}.");

        var rpRepo = _uow.Repository<T_ROLE_PERMISSION>();
        var existing = await rpRepo.ListAsync(p => p.IDROLE == targetRoleId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PERMISSION_CODE != EmptyRolePermissionsMarker)
            .Select(p => p.PERMISSION_CODE)
            .ToList();

        rpRepo.RemoveRange(existing);

        foreach (var p in normalizedPermissions)
        {
            await rpRepo.AddAsync(new T_ROLE_PERMISSION
            {
                IDROLE = targetRoleId,
                PERMISSION_CODE = p,
                IS_GRANTED = true
            }, cancellationToken);
        }

        if (normalizedPermissions.Count == 0)
        {
            await rpRepo.AddAsync(new T_ROLE_PERMISSION
            {
                IDROLE = targetRoleId,
                PERMISSION_CODE = EmptyRolePermissionsMarker,
                IS_GRANTED = true
            }, cancellationToken);
        }

        var droitRepo = _uow.Repository<T_DROIT_ROLE>();
        var existingDroits = await droitRepo.ListAsync(d => d.IDROLE == targetRoleId, cancellationToken);
        droitRepo.RemoveRange(existingDroits);

        var portesDeduites = DeduirePortes(normalizedPermissions);
        foreach (var porte in portesDeduites)
        {
            await droitRepo.AddAsync(new T_DROIT_ROLE { IDROLE = targetRoleId, IDDROIT = porte }, cancellationToken);
        }

        await EcrireAuditDroitAsync(
            currentUserId, caller.LOGIN ?? string.Empty,
            "ROLE", targetRoleId, targetRole.NOMROLE,
            "UPDATE_ROLE_PERMS",
            permsAvant, normalizedPermissions,
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    public async Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var caller = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Utilisateur connecté introuvable.");

        var target = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur cible introuvable.");

        var callerIsAdmin = AdminRules.IsSuperAdmin(caller.IDROLE);

        if (currentUserId == targetUserId && !callerIsAdmin)
        {
            throw new InvalidOperationException("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.");
        }

        var repo = _uow.Repository<T_USER_PERMISSION>();
        var existing = await repo.ListAsync(p => p.IDRESPONSABLE == targetUserId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PERMISSION_CODE != EmptyPermissionsMarker)
            .Select(p => p.PERMISSION_CODE)
            .ToList();

        repo.RemoveRange(existing);

        await EcrireAuditDroitAsync(
            currentUserId, caller.LOGIN ?? string.Empty,
            "USER", targetUserId, target.NOMRESPONSABLE,
            "RESET_USER_PERMS",
            permsAvant, null,
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    public async Task<IReadOnlyList<AuditDroitDto>> GetAuditDroitsAsync(
        string? typeCible, decimal? idCible, DateTime? from, DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow.AddDays(1);
        var list = await _uow.Repository<T_AUDIT_DROIT>().ListLatestAsync(
            a => a.DATE_ACTION >= fromDate
                 && a.DATE_ACTION <= toDate
                 && (string.IsNullOrEmpty(typeCible) || a.TYPE_CIBLE == typeCible)
                 && (!idCible.HasValue || a.ID_CIBLE == idCible.Value),
            a => (DateTime?)a.DATE_ACTION,
            take: 500,
            cancellationToken);

        return list.Select(a => new AuditDroitDto(
            a.ID, a.DATE_ACTION, a.ID_OPERATEUR, a.LOGIN_OPERATEUR,
            a.TYPE_CIBLE, a.ID_CIBLE, a.NOM_CIBLE,
            a.ACTION, a.PERMS_AVANT, a.PERMS_APRES)).ToList();
    }
}

