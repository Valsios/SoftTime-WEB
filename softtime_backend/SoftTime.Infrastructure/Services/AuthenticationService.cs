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

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;
    private readonly IEmailService _email;
    private readonly IPasswordHasher<Responsable> _passwordHasher;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly AccountSecurityOptions _security;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly IUserPermissionService _userPermissionService;

    public AuthenticationService(
        IUnitOfWork uow,
        ITokenService tokens,
        IMapper mapper,
        IEmailService email,
        IEffectiveRightsService effectiveRights,
        IUserPermissionService userPermissionService,
        IPasswordHasher<Responsable>? passwordHasher = null,
        IRefreshTokenService? refreshTokens = null,
        IOptions<AccountSecurityOptions>? securityOptions = null,
        ILogger<AuthenticationService>? logger = null)
    {
        _uow = uow;
        _tokens = tokens;
        _mapper = mapper;
        _email = email;
        _passwordHasher = passwordHasher ?? new PasswordHasher<Responsable>();
        _effectiveRights = effectiveRights;
        _refreshTokens = refreshTokens ?? new RefreshTokenService(uow, securityOptions);
        _security = securityOptions?.Value ?? new AccountSecurityOptions();
        _logger = logger ?? NullLogger<AuthenticationService>.Instance;
        _userPermissionService = userPermissionService;
    }

    public const string InvalidCredentialsMessage = "Identifiants invalides.";

    public const string LockedAccountMessage =
        "Compte temporairement verrouillé après plusieurs tentatives échouées. Réessayez dans {0} minute(s).";

    // Empreinte factice vérifiée quand le login est inconnu : même coût de calcul qu'un compte existant (pas d'énumération par la durée).
    private static readonly ConcurrentDictionary<Type, string> DummyHashes = new();

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
        var detailed = await _userPermissionService.GetUserDetailedPermissionsAsync(user.IdResponsable, cancellationToken);
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
        var detailed = await _userPermissionService.GetUserDetailedPermissionsAsync(user.IdResponsable, cancellationToken);
        var authorizedSages = await UsableSageDatabasesAsync(user, effective, cancellationToken);
        return new LoginResponse(string.Empty, user.IdResponsable, user.Login ?? string.Empty, user.NomResponsable ?? string.Empty, user.IdRole, user.Matricule, rights, authorizedSages.Select(_mapper.Map<SageDbDto>).ToList(), detailed.GrantedPermissions, detailed.IsCustomized);
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
}
