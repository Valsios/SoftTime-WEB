using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Domain;

namespace SoftTime.Infrastructure.Services;

public class AuthService : IAuthService
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
        "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING"
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
        IEnumerable<BddSage> sageDbs,
        IEnumerable<BddPointeuse> pointeuseDbs)
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
        await _uow.Repository<AuditDroit>().AddAsync(new AuditDroit
        {
            DateAction = DateTime.UtcNow,
            IdOperateur = idOperateur,
            LoginOperateur = Truncate(loginOperateur, 100) ?? string.Empty,
            TypeCible = Truncate(typeCible, 20) ?? string.Empty,
            IdCible = idCible,
            NomCible = Truncate(nomCible, 100),
            Action = Truncate(action, 50) ?? string.Empty,
            PermsAvant = avant,
            PermsApres = apres
        }, cancellationToken);
    }

    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;
    private readonly IEmailService _email;
    private readonly IPasswordHasher<Responsable> _passwordHasher;
    // Appelant HTTP, obligatoire : aucune écriture sur un autre compte sans contrôle d'administrateur.
    private readonly ICurrentUser _currentUser;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly AccountSecurityOptions _security;
    private readonly ILogger<AuthService> _logger;

    public const string InvalidCredentialsMessage = "Identifiants invalides.";
    public const string LockedAccountMessage =
        "Compte temporairement verrouillé après plusieurs tentatives échouées. Réessayez dans {0} minute(s).";

    // Empreinte factice vérifiée quand le login est inconnu : même coût de calcul qu'un compte existant (pas d'énumération par la durée).
    private static readonly ConcurrentDictionary<Type, string> DummyHashes = new();

    /// <summary>
    /// Consultation sensible : administrateur, ou porte 2 ET l'une des permissions fines, lues dans les droits effectifs
    /// (pas dans le jeton). Échec fermé si la lecture des droits échoue.
    /// </summary>
    public async Task EnsureCanReadAsync(string[] anyOf, CancellationToken cancellationToken = default)
    {
        if (AdminAccess.IsAdmin(_currentUser)) return;
        if (_currentUser.UserId is not decimal uid)
            throw new ForbiddenException(ReadPermissions.Denied);
        EffectiveRights rights;
        try
        {
            rights = await _effectiveRights.GetAsync(uid, ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _effectiveRights.LogReadFailure(uid, ex);
            throw new ForbiddenException(ReadPermissions.Denied);
        }
        if (!rights.HasGate(Rights.Parameters) || !anyOf.Any(rights.Has))
            throw new ForbiddenException(ReadPermissions.Denied);
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
        IEffectiveRightsService effectiveRights,
        IPasswordHasher<Responsable>? passwordHasher = null,
        IRefreshTokenService? refreshTokens = null,
        IOptions<AccountSecurityOptions>? securityOptions = null,
        ILogger<AuthService>? logger = null)
    {
        _logger = logger ?? NullLogger<AuthService>.Instance;
        _uow = uow;
        _tokens = tokens;
        _mapper = mapper;
        _email = email;
        _passwordHasher = passwordHasher ?? new PasswordHasher<Responsable>();
        _currentUser = currentUser;
        _effectiveRights = effectiveRights;
        _security = securityOptions?.Value ?? new AccountSecurityOptions();
        _refreshTokens = refreshTokens ?? new RefreshTokenService(uow, securityOptions);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var login = (request.Login ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
            throw new UnauthorizedException(InvalidCredentialsMessage);

        var user = await FindByLoginAsync(login, cancellationToken);
        if (user is null)
        {
            _passwordHasher.VerifyHashedPassword(new Responsable(), DummyHash(), password);
            _logger.LogWarning("Connexion refusée : login inconnu ou ambigu « {Login} ».", LogSafe(login));
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }
        if (IsLocked(user))
            _logger.LogWarning("Connexion refusée : compte {UserId} verrouillé jusqu'à {LockedUntil:o}.", user.IdResponsable, user.VerrouilleJusqua);
        EnsureNotLocked(user);

        var (passwordVerified, needsUpgrade) = VerifyPassword(user, password);
        if (!passwordVerified)
        {
            _logger.LogWarning("Connexion refusée : mot de passe incorrect pour le compte {UserId}.", user.IdResponsable);
            await RegisterFailedLoginAsync(user, cancellationToken);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (needsUpgrade)
            user.Password = _passwordHasher.HashPassword(user, password);
        if (needsUpgrade || user.TentativesEchouees != 0 || user.VerrouilleJusqua is not null)
        {
            user.TentativesEchouees = 0;
            user.VerrouilleJusqua = null;
            _uow.Repository<Responsable>().Update(user);
        }

        var refreshToken = await _refreshTokens.IssueAsync(user.IdResponsable, cancellationToken: cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return await BuildSessionAsync(user, refreshToken, cancellationToken);
    }

    /// <summary>
    /// Rafraîchit la session : le jeton présenté est échangé contre un nouveau (rotation) et le jeton d'accès
    /// est réémis avec les droits effectifs actuels (un droit retiré disparaît au plus tard au rafraîchissement suivant).
    /// </summary>
    public async Task<LoginResponse> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var rotation = await _refreshTokens.RotateAsync(refreshToken, cancellationToken);
        var user = await _uow.Repository<Responsable>().GetByIdAsync(rotation.UserId, cancellationToken);
        if (user is null || IsLocked(user))
            throw new UnauthorizedException(RefreshTokenService.InvalidTokenMessage);
        await _uow.SaveChangesAsync(cancellationToken);
        return await BuildSessionAsync(user, rotation.RefreshToken, cancellationToken);
    }

    /// <summary>Déconnexion : le jeton de rafraîchissement présenté ne peut plus servir.</summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        await _refreshTokens.RevokeAsync(refreshToken, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private async Task<LoginResponse> BuildSessionAsync(Responsable user, string refreshToken, CancellationToken cancellationToken)
    {
        var login = user.Login ?? string.Empty;
        var (rights, effective) = await TokenGatesAsync(user, cancellationToken);
        var token = _tokens.CreateToken(user.IdResponsable, login, user.IdRole, user.Matricule, rights);
        var detailed = await GetUserDetailedPermissionsAsync(user.IdResponsable, cancellationToken);
        var authorizedSages = await UsableSageDatabasesAsync(user, effective, cancellationToken);
        return new LoginResponse(token, user.IdResponsable, login, user.NomResponsable ?? login, user.IdRole, user.Matricule, rights,
            authorizedSages.Select(_mapper.Map<SageDbDto>).ToList(), detailed.GrantedPermissions, detailed.IsCustomized, refreshToken);
    }

    /// <summary>
    /// Une seule requête, sans casse ni espaces en bordure (voir <see cref="LoginKey"/>). Si une ancienne base
    /// contient encore des doublons (index unique de la migration 005 non posé), le compte dont le login est
    /// exactement celui saisi l'emporte ; sinon la connexion est refusée plutôt que d'ouvrir un compte au hasard.
    /// </summary>
    private async Task<Responsable?> FindByLoginAsync(string login, CancellationToken cancellationToken)
    {
        var users = await _uow.Repository<Responsable>().ListAsync(LoginKey.Matches(LoginKey.Normalize(login)), cancellationToken);
        if (users.Count <= 1)
            return users.FirstOrDefault();
        var exact = users.Where(u => string.Equals((u.Login ?? string.Empty).Trim(), login, StringComparison.Ordinal)).ToList();
        return exact.Count == 1 ? exact[0] : null;
    }

    private (bool Verified, bool NeedsUpgrade) VerifyPassword(Responsable user, string password)
    {
        if (string.IsNullOrEmpty(user.Password))
            return (false, false);
        try
        {
            var result = _passwordHasher.VerifyHashedPassword(user, user.Password, password);
            if (result == PasswordVerificationResult.Success)
                return (true, false);
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                return (true, true);
        }
        catch (FormatException)
        {
            // Valeur stockée qui n'est pas un hachage connu : seul le cas « mot de passe historique en clair » reste possible.
        }
        // Mot de passe historique en clair : comparaison à temps constant, puis hachage immédiat.
        var legacyMatch = PasswordHashClassifier.IsPlainText(user.Password)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.Password), Encoding.UTF8.GetBytes(password));
        return (legacyMatch, legacyMatch);
    }

    private static bool IsLocked(Responsable user)
        => user.VerrouilleJusqua is DateTime until && until > DateTime.UtcNow;

    private static void EnsureNotLocked(Responsable user)
    {
        if (!IsLocked(user))
            return;
        var minutes = Math.Max(1, (int)Math.Ceiling((user.VerrouilleJusqua!.Value - DateTime.UtcNow).TotalMinutes));
        throw new ForbiddenException(string.Format(CultureInfo.InvariantCulture, LockedAccountMessage, minutes));
    }

    /// <summary>Compte un échec ; au seuil, verrouille le compte et remet le compteur à zéro pour la période suivante.</summary>
    private async Task RegisterFailedLoginAsync(Responsable user, CancellationToken cancellationToken)
    {
        user.TentativesEchouees++;
        if (user.TentativesEchouees >= _security.MaxFailedLoginAttempts)
        {
            user.TentativesEchouees = 0;
            user.VerrouilleJusqua = DateTime.UtcNow.AddMinutes(_security.LockoutMinutes);
            _logger.LogWarning("Compte {UserId} verrouillé {Minutes} min après {Attempts} échecs de connexion.",
                user.IdResponsable, _security.LockoutMinutes, _security.MaxFailedLoginAttempts);
        }
        _uow.Repository<Responsable>().Update(user);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Login saisi, tel qu'il peut être journalisé : sans caractère de contrôle (pas de fausse ligne injectée dans le
    /// journal) et tronqué (un mot de passe tapé par erreur dans le champ login n'y apparaît pas en entier).
    /// </summary>
    internal static string LogSafe(string value)
    {
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length <= 32 ? clean : clean[..32] + "…";
    }

    private string DummyHash()
        => DummyHashes.GetOrAdd(_passwordHasher.GetType(), _ => _passwordHasher.HashPassword(new Responsable(), Guid.NewGuid().ToString("N")));

    /// <summary>
    /// Portes du claim « rights » : celles des droits effectifs du compte (rôle + ajouts − retraits), lues à la connexion.
    /// L'administrateur (rôle 1) a les portes 1 à 6. Un changement de droits ne s'applique qu'à la reconnexion.
    /// </summary>
    private async Task<(List<int> Gates, EffectiveRights? Effective)> TokenGatesAsync(Responsable user, CancellationToken cancellationToken)
    {
        if (user.IdRole == AdminRules.AdminRoleId)
            return (new List<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports }, null);
        var effective = await _effectiveRights.GetAsync(user.IdResponsable, fresh: true, ct: cancellationToken);
        return (effective.Gates.OrderBy(g => g).ToList(), effective);
    }

    /// <summary>Bases SAGE proposées au compte : exactement celles qu'il peut utiliser (même règle que le contrôle serveur).</summary>
    private async Task<List<BddSage>> UsableSageDatabasesAsync(Responsable user, EffectiveRights? effective, CancellationToken cancellationToken)
    {
        var sages = await _uow.Repository<BddSage>().ListAsync(_ => true, cancellationToken);
        if (effective is null)
            return sages;
        return sages.Where(s => effective.CanUseDatabase(DatabaseKind.Sage, s.NomBd)).ToList();
    }

    public async Task<LoginResponse> GetMeAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur introuvable.");
        var (rights, effective) = await TokenGatesAsync(user, cancellationToken);
        var detailed = await GetUserDetailedPermissionsAsync(user.IdResponsable, cancellationToken);
        var authorizedSages = await UsableSageDatabasesAsync(user, effective, cancellationToken);
        return new LoginResponse(string.Empty, user.IdResponsable, user.Login ?? string.Empty, user.NomResponsable ?? string.Empty, user.IdRole, user.Matricule, rights, authorizedSages.Select(_mapper.Map<SageDbDto>).ToList(), detailed.GrantedPermissions, detailed.IsCustomized);
    }

    public async Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.Users, cancellationToken);
        var u = await _uow.Repository<Responsable>().GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();
        return new UserDto(u.IdResponsable, u.IdRole, u.NomResponsable ?? string.Empty, u.Login ?? string.Empty, u.Matricule, null) { Email = u.Email };
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.UserList, cancellationToken);
        var list = await _uow.Repository<Responsable>().ListReadOnlyAsync(_ => true, cancellationToken);
        return list
            .OrderBy(u => u.NomResponsable)
            .ThenBy(u => u.Login)
            .Select(u => new UserDto(u.IdResponsable, u.IdRole, u.NomResponsable ?? string.Empty, u.Login ?? string.Empty, u.Matricule, null) { Email = u.Email })
            .ToList();
    }

    public async Task<PagedResult<UserDto>> GetUsersPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.Users, cancellationToken);
        decimal[] roleIds = Array.Empty<decimal>();
        if (SearchText.Terms(search).Count > 0)
        {
            roleIds = (await _uow.Repository<Role>().ListReadOnlyAsync(_ => true, cancellationToken))
                .Where(role => SearchText.Matches(search, role.NomRole))
                .Select(role => role.IdRole)
                .ToArray();
        }

        var searchPredicate = SearchExpression.For<Responsable>(
            search,
            u => u.NomResponsable,
            u => u.Login,
            u => u.Email,
            u => u.Matricule);
        System.Linq.Expressions.Expression<Func<Responsable, bool>> predicate =
            searchPredicate ?? (_ => true);
        if (roleIds.Length > 0)
        {
            System.Linq.Expressions.Expression<Func<Responsable, bool>> rolePredicate =
                user => roleIds.Contains(user.IdRole);
            predicate = searchPredicate is null
                ? rolePredicate
                : SearchExpression.OrElse(searchPredicate, rolePredicate);
        }

        return await PagedLists.PageAsync(
            _uow.Repository<Responsable>(),
            predicate,
            q => q.OrderBy(u => u.NomResponsable).ThenBy(u => u.Login).ThenBy(u => u.IdResponsable),
            page,
            pageSize,
            u => new UserDto(u.IdResponsable, u.IdRole, u.NomResponsable ?? string.Empty, u.Login ?? string.Empty, u.Matricule, null) { Email = u.Email },
            cancellationToken);
    }

    /// <summary>
    /// Installation vierge ? Portage de <c>is-first-run</c> du desktop, avec un critère plus sain :
    /// le desktop cherchait un rôle nommé « administrateur », ici on constate simplement qu'aucun
    /// compte n'existe. Le critère ne dépend donc pas d'une convention de nommage des rôles, et il
    /// reste vrai après la suppression du dernier compte (seul moyen de sortir d'un verrouillage total).
    /// </summary>
    public async Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<Responsable>();
        return await repo.CountAsync(repo.QueryReadOnly().Take(1), cancellationToken) == 0;
    }

    /// <summary>Verrou applicatif de la création initiale : deux appels simultanés passent l'un après l'autre.</summary>
    internal const string CreateAdminLockResource = "SoftTime.CreateAdmin";

    /// <summary>
    /// Crée le premier compte administrateur. Refusé dès qu'un compte existe, et le rôle
    /// <see cref="RoleAdministrateur"/> se voit garantir tous les droits : sans cela, l'application
    /// serait inaccessible juste après son installation.
    /// </summary>
    public async Task<AdminSetupResultDto> CreateAdminAsync(AdminSetupDto dto, CancellationToken cancellationToken = default)
    {
        if (!await IsFirstRunAsync(cancellationToken))
            throw new DomainException(
                "Des comptes existent déjà : la création initiale n'est plus possible. Utilisez l'écran Utilisateurs.");
        var login = (dto.Login ?? string.Empty).Trim();
        var motDePasse = dto.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dto.Nom))
            throw new DomainException("Le nom est requis.");
        if (string.IsNullOrWhiteSpace(login))
            throw new DomainException("L'identifiant est requis.");
        if (string.IsNullOrWhiteSpace(motDePasse))
            throw new DomainException("Le mot de passe est requis.");
        PasswordPolicy.EnsureValid(motDePasse);
        var passwordHash = _passwordHasher.HashPassword(null!, motDePasse);

        // La vérification ci-dessus ne suffit pas : deux requêtes simultanées sur une base vide la passeraient
        // toutes les deux. Sous le verrou, la seconde attend la fin de la première, puis voit le compte créé.
        var entity = await _uow.RunExclusiveAsync(CreateAdminLockResource, async ct =>
        {
            if (!await IsFirstRunAsync(ct))
                throw new DomainException(
                    "Des comptes existent déjà : la création initiale n'est plus possible. Utilisez l'écran Utilisateurs.");

            // Le premier compte reçoit toujours le rôle d'identifiant 1 : seul ce rôle est administrateur.
            var role = await _uow.Repository<Role>().GetByIdAsync(AdminRules.AdminRoleId, ct)
                ?? throw new DomainException(
                    "Le rôle Administrateur (identifiant 1) est introuvable : exécutez le script de création de la base.");

            var created = new Responsable
            {
                IdRole = role.IdRole,
                NomResponsable = dto.Nom.Trim(),
                Login = login,
                Password = passwordHash,
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim()
            };
            await _uow.Repository<Responsable>().AddAsync(created, ct);
            await EnsureRoleHasAllRightsAsync(role.IdRole, ct);
            await _uow.SaveChangesAsync(ct);
            return created;
        }, cancellationToken);

        var envoye = await _email.SendUserCredentialsAsync(entity.Email, entity.Login, motDePasse, entity.NomResponsable, null, cancellationToken);
        var message = envoye
            ? "Compte administrateur créé et identifiants envoyés par e-mail."
            : _email.EstConfigure
                ? "Compte administrateur créé. L'envoi de l'e-mail a échoué : communiquez les identifiants manuellement."
                : "Compte administrateur créé. Aucune configuration SMTP (« Email ») : communiquez les identifiants manuellement.";
        return new AdminSetupResultDto(entity.IdResponsable, entity.Login ?? login, envoye, message);
    }

    /// <summary>Garantit que le rôle d'administration détient tous les droits (idempotent).</summary>
    private async Task EnsureRoleHasAllRightsAsync(decimal roleId, CancellationToken cancellationToken)
    {
        var droits = await _uow.Repository<DroitAcces>().ListAsync(_ => true, cancellationToken);
        var existants = await _uow.Repository<DroitRole>().ListAsync(p => p.IdRole == roleId, cancellationToken);
        var manquants = droits.Where(d => existants.All(e => e.IdDroit != d.IdDroit)).ToList();
        if (manquants.Count == 0)
            return;
        var repo = _uow.Repository<DroitRole>();
        foreach (var droit in manquants)
            await repo.AddAsync(new DroitRole { IdRole = roleId, IdDroit = droit.IdDroit }, cancellationToken);
    }

    public async Task<UserDto> SaveUserAsync(decimal currentUserId, UserDto dto, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
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
                    .Where(p => p.PermissionCode != EmptyPermissionsMarker)
                    .Select(p => p.PermissionCode)
                    .ToList();
                if (existingPerms.Count > 0)
                {
                    permRepo.RemoveRange(existingPerms);
                    await EcrireAuditDroitAsync(
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

    /// <summary>
    /// Aperçu d'un changement de rôle : ajustements personnels qui seraient effacés. Lecture seule, administrateur seulement.
    /// Si le rôle ne change pas ou si le compte n'a aucun ajustement, Count vaut 0.
    /// </summary>
    public async Task<RoleChangePreviewDto> PreviewRoleChangeAsync(decimal userId, decimal newRoleId, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException();
        var login = user.Login ?? string.Empty;
        if (user.IdRole == newRoleId)
            return new RoleChangePreviewDto(Array.Empty<string>(), 0, login);
        var adjustments = await _effectiveRights.AdjustmentsAsync(userId, cancellationToken);
        return new RoleChangePreviewDto(adjustments, adjustments.Count, login);
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

    public async Task DeleteUserAsync(decimal currentUserId, decimal id, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
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

        if (entity.IdRole == AdminRules.AdminRoleId)
        {
            if (!callerIsSuperAdmin)
                throw new DomainException("Seul un super-administrateur peut supprimer un compte administrateur.");
        }

        if (entity.IdRole == AdminRules.AdminRoleId)
        {
            var otherAdmins = await repo.ListAsync(u => u.IdRole == AdminRules.AdminRoleId && u.IdResponsable != entity.IdResponsable, cancellationToken);
            if (otherAdmins.Count == 0)
                throw new DomainException("Impossible de supprimer le dernier administrateur.");
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

    /// <summary>Réservé aux tests (internal, sans garde) : le code de production doit transmettre l'identité de l'appelant.</summary>
    internal async Task DeleteUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<Responsable>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();
        if (entity.IdRole == AdminRules.AdminRoleId)
        {
            var otherAdmins = await repo.ListAsync(u => u.IdRole == AdminRules.AdminRoleId && u.IdResponsable != entity.IdResponsable, cancellationToken);
            if (otherAdmins.Count == 0)
                throw new DomainException("Impossible de supprimer le dernier administrateur.");
        }
        await RemoveAccountRightsAsync(entity.IdResponsable, cancellationToken);
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(id);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.RoleList, cancellationToken);
        return (await _uow.Repository<Role>().ListReadOnlyAsync(_ => true, cancellationToken))
            .Select(r => new RoleDto(r.IdRole, r.NomRole)).ToList();
    }

    public async Task<PagedResult<RoleDto>> GetRolesPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.Roles, cancellationToken);
        return await PagedLists.PageAsync(
            _uow.Repository<Role>(),
            _ => true,
            q => q.OrderBy(r => r.IdRole),
            page,
            pageSize,
            r => new RoleDto(r.IdRole, r.NomRole),
            search,
            new System.Linq.Expressions.Expression<Func<Role, string?>>[] { r => r.NomRole },
            cancellationToken);
    }

    public async Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id == AdminRules.AdminRoleId)
            throw new DomainException("Le rôle Administrateur ne peut pas être modifié ni renommé.");
        var repo = _uow.Repository<Role>();
        Role entity;
        if (dto.Id == 0)
        {
            entity = new Role { NomRole = dto.Nom };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new NotFoundException();
            entity.NomRole = dto.Nom;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
        return new RoleDto(entity.IdRole, entity.NomRole);
    }

    public async Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default)
    {
        if (id == AdminRules.AdminRoleId)
            throw new DomainException("Le rôle Administrateur ne peut pas être supprimé.");
        var repo = _uow.Repository<Role>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();

        // C5 — Nettoyage à la suppression d'un rôle
        var rpRepo = _uow.Repository<RolePermission>();
        var rolePerms = await rpRepo.ListAsync(p => p.IdRole == id, cancellationToken);
        rpRepo.RemoveRange(rolePerms);

        var droitRepo = _uow.Repository<DroitRole>();
        var droits = await droitRepo.ListAsync(d => d.IdRole == id, cancellationToken);
        droitRepo.RemoveRange(droits);

        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    public async Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.PrivilegeList, cancellationToken);
        return (await _uow.Repository<DroitAcces>().ListReadOnlyAsync(_ => true, cancellationToken))
            .Select(d => new DroitDto(d.IdDroit, d.Droit)).ToList();
    }

    public async Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.PrivilegeList, cancellationToken);
        var list = await _uow.Repository<DroitRole>().ListReadOnlyAsync(
            p => !roleId.HasValue || p.IdRole == roleId.Value, cancellationToken);
        return list.Select(p => new PrivilegeDto(p.IdDroitRole, p.IdRole, p.IdDroit)).ToList();
    }

    public async Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default)
    {
        if (roleId == AdminRules.AdminRoleId)
            throw new DomainException("Les privilèges du rôle Administrateur ne sont pas modifiables.");
        // Les portes d'un rôle ayant des droits détaillés se déduisent de ces droits (SaveRoleDetailedPermissionsAsync) : écrire
        // T_DROIT_ROLE à la main n'aurait aucun effet sur le jeton et le contredirait.
        if ((await _uow.Repository<RolePermission>().ListAsync(p => p.IdRole == roleId, cancellationToken)).Count > 0)
            throw new DomainException("Ce rôle a des droits détaillés : modifiez-les depuis la page Privilèges, les portes en sont déduites.");
        var repo = _uow.Repository<DroitRole>();
        var existing = await repo.ListAsync(p => p.IdRole == roleId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var d in droitIds.Distinct())
            await repo.AddAsync(new DroitRole { IdRole = roleId, IdDroit = d }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    public async Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.DbAccess, cancellationToken);
        var list = await _uow.Repository<BddAutorisee>().ListReadOnlyAsync(
            a => !userId.HasValue || a.IdResponsable == userId.Value, cancellationToken);
        return list.Select(a => new DbAccessDto(a.IdBddAutorise, a.IdResponsable, a.IdBdd)).ToList();
    }

    public async Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var repo = _uow.Repository<BddAutorisee>();
        var existing = await repo.ListAsync(a => a.IdResponsable == userId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var id in sageDbIds.Distinct())
            await repo.AddAsync(new BddAutorisee { IdResponsable = userId, IdBdd = id }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(userId);
    }

    public async Task<UserDetailedPermissionsDto> GetUserDetailedPermissionsAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<Responsable>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur introuvable.");

        var customList = await _uow.Repository<UserPermission>().ListReadOnlyAsync(
            p => p.IdResponsable == userId && p.IsGranted, cancellationToken);

        var rolePrivileges = await _uow.Repository<DroitRole>().ListReadOnlyAsync(p => p.IdRole == user.IdRole, cancellationToken);
        var rights = rolePrivileges.Select(p => (int)p.IdDroit).ToHashSet();
        var isAdmin = user.IdRole == 1;
        if (isAdmin)
        {
            rights = new HashSet<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        }

        var access = await _uow.Repository<BddAutorisee>().ListReadOnlyAsync(a => a.IdResponsable == userId, cancellationToken);
        var sageIds = access.Select(a => a.IdBdd).ToHashSet();
        // Admin : toutes les bases ; autres : seulement celles de T_BDD_AUTORISE.
        var sageDbs = (isAdmin || rights.Contains(Rights.Databases))
            ? await _uow.Repository<BddSage>().ListReadOnlyAsync(_ => true, cancellationToken)
            : await _uow.Repository<BddSage>().ListReadOnlyAsync(s => sageIds.Contains(s.Id), cancellationToken);
        var pointeuseDbs = await _uow.Repository<BddPointeuse>().ListReadOnlyAsync(_ => true, cancellationToken);

        var defaults = BuildDefaultPermissionsList(rights, sageDbs, pointeuseDbs);

        if (customList.Count > 0)
        {
            var perms = customList
                .Where(p => p.PermissionCode != EmptyPermissionsMarker)
                .Select(p => p.PermissionCode)
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
        var roleCustomPerms = await _uow.Repository<RolePermission>().ListReadOnlyAsync(
            p => p.IdRole == user.IdRole && p.IsGranted, cancellationToken);
        if (roleCustomPerms.Count > 0)
        {
            var rolePerms = roleCustomPerms
                .Where(p => p.PermissionCode != EmptyRolePermissionsMarker)
                .Select(p => p.PermissionCode)
                .Distinct()
                .ToList();
            if (isAdmin)
            {
                rolePerms.AddRange(defaults);
                rolePerms = rolePerms.Distinct().ToList();
            }
            return new UserDetailedPermissionsDto(userId, rolePerms, false);
        }

        // Rôle sans lignes et compte sans ajustement : les droits affichés sont les droits effectifs du calculateur
        // (sans la configuration des bases). L'administrateur garde ses droits par défaut complets.
        if (!isAdmin)
            return new UserDetailedPermissionsDto(userId, (await _effectiveRights.GetAsync(userId, fresh: true, ct: cancellationToken)).Permissions.ToList(), false);
        return new UserDetailedPermissionsDto(userId, defaults.Distinct().ToList(), false);
    }

    public async Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new UnauthorizedException("Utilisateur connecté introuvable.");

        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var callerIsAdmin = AdminRules.IsSuperAdmin(caller.IdRole);

        if (currentUserId == targetUserId && !callerIsAdmin)
        {
            throw new DomainException("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.");
        }

        var targetIsAdmin = target.IdRole == 1;

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

            var allSages = await _uow.Repository<BddSage>().ListAsync(_ => true, cancellationToken);
            foreach (var s in allSages)
            {
                var n = s.NomBd ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n)) continue;
                toSaveList.Add($"SAGE:{n}:READ");
                toSaveList.Add($"SAGE:{n}:UPDATE");
                toSaveList.Add($"SAGE:{n}:DELETE");
            }

            var allPointeuses = await _uow.Repository<BddPointeuse>().ListAsync(_ => true, cancellationToken);
            foreach (var p in allPointeuses)
            {
                var n = p.NomBd ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n)) continue;
                toSaveList.Add($"POINTEUSE:{n}:READ");
                toSaveList.Add($"POINTEUSE:{n}:UPDATE");
                toSaveList.Add($"POINTEUSE:{n}:DELETE");
            }
        }

        var repo = _uow.Repository<UserPermission>();
        var existing = await repo.ListAsync(p => p.IdResponsable == targetUserId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PermissionCode != EmptyPermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();
        repo.RemoveRange(existing);

        foreach (var p in toSaveList.Distinct())
        {
            await repo.AddAsync(new UserPermission
            {
                IdResponsable = targetUserId,
                PermissionCode = p.Trim(),
                IsGranted = true
            }, cancellationToken);
        }

        // Une ligne sentinelle conserve l'intention "aucun privilège" : sans elle,
        // l'absence de ligne serait interprétée comme un retour aux droits du rôle.
        if (toSaveList.Count == 0)
        {
            await repo.AddAsync(new UserPermission
            {
                IdResponsable = targetUserId,
                PermissionCode = EmptyPermissionsMarker,
                IsGranted = true
            }, cancellationToken);
        }

        await EcrireAuditDroitAsync(
            currentUserId, caller.Login ?? string.Empty,
            "USER", targetUserId, target.NomResponsable,
            "UPDATE_USER_PERMS",
            permsAvant, toSaveList,
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.Invalidate(targetUserId);
    }

    public async Task<RoleDetailedPermissionsDto> GetRoleDetailedPermissionsAsync(decimal roleId, CancellationToken cancellationToken = default)
    {
        await EnsureCanReadAsync(ReadPermissions.Privileges, cancellationToken);
        var roleList = await _uow.Repository<RolePermission>().ListReadOnlyAsync(
            p => p.IdRole == roleId && p.IsGranted, cancellationToken);

        if (roleList.Count == 0)
        {
            var defaultPerms = (await _effectiveRights.RoleDefaultPermissionsAsync(roleId, cancellationToken)).ToList();
            return new RoleDetailedPermissionsDto(roleId, defaultPerms, false);
        }

        var perms = roleList
            .Where(p => p.PermissionCode != EmptyRolePermissionsMarker)
            .Select(p => p.PermissionCode)
            .Distinct()
            .ToList();

        return new RoleDetailedPermissionsDto(roleId, perms, true);
    }

    public async Task SaveRoleDetailedPermissionsAsync(decimal currentUserId, decimal targetRoleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        if (permissions == null)
            throw new DomainException("La liste des permissions ne peut pas être nulle.");

        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new DomainException("Utilisateur appelant introuvable.");

        var callerRole = await _uow.Repository<Role>().GetByIdAsync(caller.IdRole, cancellationToken)
            ?? throw new DomainException("Utilisateur appelant introuvable.");

        if (!AdminRules.IsSuperAdmin(caller.IdRole))
            throw new DomainException("Seul un administrateur peut modifier les permissions d'un rôle.");

        if (targetRoleId == AdminRules.AdminRoleId)
            throw new DomainException("Les privilèges du rôle Administrateur ne sont pas modifiables.");

        var targetRole = await _uow.Repository<Role>().GetByIdAsync(targetRoleId, cancellationToken)
            ?? throw new NotFoundException("Rôle cible introuvable.");

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
            throw new DomainException($"Code de permission trop long (128 caractères maximum) : {string.Join(", ", tooLongCodes)}.");

        var invalidCodes = normalizedPermissions
            .Where(p => !IsKnownPermissionCode(p))
            .ToList();
        if (invalidCodes.Count > 0)
            throw new DomainException($"Codes de permission inconnus : {string.Join(", ", invalidCodes)}.");

        var rpRepo = _uow.Repository<RolePermission>();
        var existing = await rpRepo.ListAsync(p => p.IdRole == targetRoleId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PermissionCode != EmptyRolePermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();

        rpRepo.RemoveRange(existing);

        foreach (var p in normalizedPermissions)
        {
            await rpRepo.AddAsync(new RolePermission
            {
                IdRole = targetRoleId,
                PermissionCode = p,
                IsGranted = true
            }, cancellationToken);
        }

        if (normalizedPermissions.Count == 0)
        {
            await rpRepo.AddAsync(new RolePermission
            {
                IdRole = targetRoleId,
                PermissionCode = EmptyRolePermissionsMarker,
                IsGranted = true
            }, cancellationToken);
        }

        var droitRepo = _uow.Repository<DroitRole>();
        var existingDroits = await droitRepo.ListAsync(d => d.IdRole == targetRoleId, cancellationToken);
        droitRepo.RemoveRange(existingDroits);

        var portesDeduites = DeduirePortes(normalizedPermissions);
        foreach (var porte in portesDeduites)
        {
            await droitRepo.AddAsync(new DroitRole { IdRole = targetRoleId, IdDroit = porte }, cancellationToken);
        }

        await EcrireAuditDroitAsync(
            currentUserId, caller.Login ?? string.Empty,
            "ROLE", targetRoleId, targetRole.NomRole,
            "UPDATE_ROLE_PERMS",
            permsAvant, normalizedPermissions,
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    public async Task ResetUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var caller = await _uow.Repository<Responsable>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new UnauthorizedException("Utilisateur connecté introuvable.");

        var target = await _uow.Repository<Responsable>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("Utilisateur cible introuvable.");

        var callerIsAdmin = AdminRules.IsSuperAdmin(caller.IdRole);

        if (currentUserId == targetUserId && !callerIsAdmin)
        {
            throw new DomainException("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.");
        }

        var repo = _uow.Repository<UserPermission>();
        var existing = await repo.ListAsync(p => p.IdResponsable == targetUserId, cancellationToken);
        var permsAvant = existing
            .Where(p => p.PermissionCode != EmptyPermissionsMarker)
            .Select(p => p.PermissionCode)
            .ToList();

        repo.RemoveRange(existing);

        await EcrireAuditDroitAsync(
            currentUserId, caller.Login ?? string.Empty,
            "USER", targetUserId, target.NomResponsable,
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
        var list = await _uow.Repository<AuditDroit>().ListLatestAsync(
            a => a.DateAction >= fromDate
                 && a.DateAction <= toDate
                 && (string.IsNullOrEmpty(typeCible) || a.TypeCible == typeCible)
                 && (!idCible.HasValue || a.IdCible == idCible.Value),
            a => (DateTime?)a.DateAction,
            take: 500,
            cancellationToken);

        return list.Select(a => new AuditDroitDto(
            a.Id, a.DateAction, a.IdOperateur, a.LoginOperateur,
            a.TypeCible, a.IdCible, a.NomCible,
            a.Action, a.PermsAvant, a.PermsApres)).ToList();
    }
}
