using AutoMapper;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain;

namespace SoftTime.Application.Services;

public class AuthService
{
    /// <summary>Rôle d'administration attendu lors du démarrage initial (voir le script de base saine).</summary>
    public const string RoleAdministrateur = "Administrateur";
    private const string EmptyPermissionsMarker = "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__";

    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;
    private readonly IEmailService _email;

    public AuthService(IUnitOfWork uow, ITokenService tokens, IMapper mapper, IEmailService email)
    {
        _uow = uow;
        _tokens = tokens;
        _mapper = mapper;
        _email = email;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var login = (request.Login ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        var users = await _uow.Repository<T_RESPONSABLE>().ListAsync(
            u => u.LOGIN == login && u.PASSWORD == password, cancellationToken);
        if (users.Count == 0)
        {
            users = (await _uow.Repository<T_RESPONSABLE>().ListAsync(_ => true, cancellationToken))
                .Where(u => string.Equals((u.LOGIN ?? string.Empty).Trim(), login, StringComparison.OrdinalIgnoreCase)
                            && (u.PASSWORD ?? string.Empty) == password)
                .ToList();
        }
        var user = users.FirstOrDefault() ?? throw new UnauthorizedAccessException("Identifiants invalides.");
        var privileges = await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == user.IDROLE, cancellationToken);
        var rights = privileges.Select(p => (int)p.IDDROIT).Distinct().ToList();
        var isAdmin = user.IDROLE == 1;
        if (isAdmin)
        {
            rights = new List<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        }
        var token = _tokens.CreateToken(user.IDRESPONSABLE, user.LOGIN ?? login, user.IDROLE, user.MATRICULE, rights);
        var access = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == user.IDRESPONSABLE, cancellationToken);
        var sageIds = access.Select(a => a.IDBDD).ToHashSet();
        // Un admin voit toujours toutes les bases SAGE configurées.
        var rightsSet = rights.ToHashSet();
        var sages = (isAdmin || rightsSet.Contains(Rights.Databases))
            ? await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, cancellationToken)
            : await _uow.Repository<T_BDD_SAGE>().ListAsync(s => sageIds.Contains(s.ID), cancellationToken);
        var detailed = await GetUserDetailedPermissionsAsync(user.IDRESPONSABLE, cancellationToken);
        var authorizedSages = detailed.IsCustomized
            ? sages.Where(s => !string.IsNullOrWhiteSpace(s.NOM_BD) && detailed.GrantedPermissions.Contains($"SAGE:{s.NOM_BD}:READ")).ToList()
            : sages;
        return new LoginResponse(token, user.IDRESPONSABLE, user.LOGIN ?? login, user.NOMRESPONSABLE ?? login, user.IDROLE, user.MATRICULE, rights, authorizedSages.Select(_mapper.Map<SageDbDto>).ToList(), detailed.GrantedPermissions, detailed.IsCustomized);
    }

    public async Task<LoginResponse> GetMeAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");
        var privileges = await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == user.IDROLE, cancellationToken);
        var rights = privileges.Select(p => (int)p.IDDROIT).Distinct().ToList();
        var isAdmin = user.IDROLE == 1;
        if (isAdmin)
        {
            rights = new List<int> { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        }
        var rightsSet = rights.ToHashSet();
        var access = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == user.IDRESPONSABLE, cancellationToken);
        var sageIds = access.Select(a => a.IDBDD).ToHashSet();
        // Admin : toutes les bases SAGE sont toujours visibles dans la topbar.
        var sages = (isAdmin || rightsSet.Contains(Rights.Databases))
            ? await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, cancellationToken)
            : await _uow.Repository<T_BDD_SAGE>().ListAsync(s => sageIds.Contains(s.ID), cancellationToken);
        var detailed = await GetUserDetailedPermissionsAsync(user.IDRESPONSABLE, cancellationToken);
        var authorizedSages = detailed.IsCustomized
            ? sages.Where(s => !string.IsNullOrWhiteSpace(s.NOM_BD) && detailed.GrantedPermissions.Contains($"SAGE:{s.NOM_BD}:READ")).ToList()
            : sages;
        return new LoginResponse(string.Empty, user.IDRESPONSABLE, user.LOGIN ?? string.Empty, user.NOMRESPONSABLE ?? string.Empty, user.IDROLE, user.MATRICULE, rights, authorizedSages.Select(_mapper.Map<SageDbDto>).ToList(), detailed.GrantedPermissions, detailed.IsCustomized);
    }

    public async Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var u = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        return new UserDto(u.IDRESPONSABLE, u.IDROLE, u.NOMRESPONSABLE ?? string.Empty, u.LOGIN ?? string.Empty, u.MATRICULE, null) { Email = u.EMAIL };
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var list = await _uow.Repository<T_RESPONSABLE>().ListAsync(_ => true, cancellationToken);
        return list
            .OrderBy(u => u.NOMRESPONSABLE)
            .ThenBy(u => u.LOGIN)
            .Select(u => new UserDto(u.IDRESPONSABLE, u.IDROLE, u.NOMRESPONSABLE ?? string.Empty, u.LOGIN ?? string.Empty, u.MATRICULE, null) { Email = u.EMAIL })
            .ToList();
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

        var roles = await _uow.Repository<T_ROLE>().ListAsync(_ => true, cancellationToken);
        var role = roles.FirstOrDefault(r => string.Equals(r.NOMROLE?.Trim(), RoleAdministrateur, StringComparison.OrdinalIgnoreCase))
            ?? roles.OrderBy(r => r.IDROLE).FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Aucun rôle n'est défini : exécutez le script de base saine (create_softtime_database.sql) avant le démarrage initial.");

        var entity = new T_RESPONSABLE
        {
            IDROLE = role.IDROLE,
            NOMRESPONSABLE = dto.Nom.Trim(),
            LOGIN = login,
            PASSWORD = motDePasse,
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

    public async Task<UserDto> SaveUserAsync(UserDto dto, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_RESPONSABLE>();
        T_RESPONSABLE entity;
        var loginModifie = false;
        var motDePasseModifie = false;
        if (dto.Id == 0)
        {
            entity = new T_RESPONSABLE
            {
                IDROLE = dto.RoleId,
                NOMRESPONSABLE = dto.Nom,
                LOGIN = dto.Login,
                PASSWORD = dto.Password ?? string.Empty,
                MATRICULE = dto.Matricule,
                EMAIL = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim()
            };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new KeyNotFoundException();
            loginModifie = !string.Equals((entity.LOGIN ?? string.Empty).Trim(), (dto.Login ?? string.Empty).Trim(), StringComparison.Ordinal);
            motDePasseModifie = !string.IsNullOrWhiteSpace(dto.Password) && dto.Password != entity.PASSWORD;
            entity.IDROLE = dto.RoleId;
            entity.NOMRESPONSABLE = dto.Nom;
            entity.LOGIN = dto.Login;
            entity.MATRICULE = dto.Matricule;
            entity.EMAIL = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            if (motDePasseModifie)
                entity.PASSWORD = dto.Password;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);

        // Notification best-effort : le service journalise et n'échoue jamais la requête.
        if (dto.Id == 0)
            await _email.SendUserCredentialsAsync(entity.EMAIL, entity.LOGIN, dto.Password, entity.NOMRESPONSABLE, null, cancellationToken);
        else if (loginModifie || motDePasseModifie)
            await _email.SendUserUpdateAsync(entity.EMAIL, entity.LOGIN, dto.Password, entity.NOMRESPONSABLE, null, loginModifie, motDePasseModifie, cancellationToken);

        return new UserDto(entity.IDRESPONSABLE, entity.IDROLE, entity.NOMRESPONSABLE, entity.LOGIN, entity.MATRICULE, null) { Email = entity.EMAIL };
    }

    public async Task DeleteUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_RESPONSABLE>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
        => (await _uow.Repository<T_ROLE>().ListAsync(_ => true, cancellationToken))
            .Select(r => new RoleDto(r.IDROLE, r.NOMROLE)).ToList();

    public async Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default)
    {
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
        return new RoleDto(entity.IDROLE, entity.NOMROLE);
    }

    public async Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_ROLE>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default)
        => (await _uow.Repository<T_DROIT>().ListAsync(_ => true, cancellationToken))
            .Select(d => new DroitDto(d.IDDROIT, d.DROIT)).ToList();

    public async Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default)
    {
        var list = await _uow.Repository<T_DROIT_ROLE>().ListAsync(
            p => !roleId.HasValue || p.IDROLE == roleId.Value, cancellationToken);
        return list.Select(p => new PrivilegeDto(p.IDDROITROLE, p.IDROLE, p.IDDROIT)).ToList();
    }

    public async Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_DROIT_ROLE>();
        var existing = await repo.ListAsync(p => p.IDROLE == roleId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var d in droitIds.Distinct())
            await repo.AddAsync(new T_DROIT_ROLE { IDROLE = roleId, IDDROIT = d }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default)
    {
        var list = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(
            a => !userId.HasValue || a.IDRESPONSABLE == userId.Value, cancellationToken);
        return list.Select(a => new DbAccessDto(a.IDBDDAUTORISE, a.IDRESPONSABLE, a.IDBDD)).ToList();
    }

    public async Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_BDD_AUTORISE>();
        var existing = await repo.ListAsync(a => a.IDRESPONSABLE == userId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var id in sageDbIds.Distinct())
            await repo.AddAsync(new T_BDD_AUTORISE { IDRESPONSABLE = userId, IDBDD = id }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
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

        var defaults = new List<string>();

        if (sageDbs.Count > 0 || rights.Contains(Rights.Databases))
        {
            defaults.Add("SAGE:CREATE");
        }

        foreach (var s in sageDbs)
        {
            var name = s.NOM_BD ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;
            defaults.Add($"SAGE:{name}:READ");
            defaults.Add($"SAGE:{name}:UPDATE");
            defaults.Add($"SAGE:{name}:DELETE");
        }

        if (rights.Contains(Rights.Databases))
        {
            defaults.Add("POINTEUSE:CREATE");
            foreach (var p in pointeuseDbs)
            {
                var name = p.NOM_BD ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                defaults.Add($"POINTEUSE:{name}:READ");
                defaults.Add($"POINTEUSE:{name}:UPDATE");
                defaults.Add($"POINTEUSE:{name}:DELETE");
            }
        }

        if (rights.Contains(Rights.Parameters))
        {
            defaults.AddRange(new[]
            {
                "PARAMS:USERS", "PARAMS:USERS:CREATE", "PARAMS:USERS:UPDATE", "PARAMS:USERS:DELETE",
                "PARAMS:ROLES", "PARAMS:ROLES:CREATE", "PARAMS:ROLES:UPDATE", "PARAMS:ROLES:DELETE",
                "PARAMS:PRIVILEGES",
                "PARAMS:DB_ACCESS",
                "PARAMS:CLOCK_PARAMS",
                "PARAMS:CORRESPONDENCE",
                "PARAMS:CARDPAIE", "PARAMS:CARDPAIE:CREATE", "PARAMS:CARDPAIE:UPDATE", "PARAMS:CARDPAIE:DELETE",
                "PARAMS:CATEGORIES", "PARAMS:CATEGORIES:CREATE", "PARAMS:CATEGORIES:UPDATE", "PARAMS:CATEGORIES:DELETE",
                "PARAMS:AFFECTATIONS", "PARAMS:AFFECTATIONS:CREATE", "PARAMS:AFFECTATIONS:UPDATE", "PARAMS:AFFECTATIONS:DELETE",
                "PARAMS:TOLERANCES", "PARAMS:TOLERANCES:CREATE", "PARAMS:TOLERANCES:UPDATE",
                "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:CREATE", "PARAMS:HOLIDAYS:UPDATE", "PARAMS:HOLIDAYS:DELETE",
                "PARAMS:MAJORATIONS", "PARAMS:MAJORATIONS:UPDATE",
                "PARAMS:CODE_CONSTANTES",
                "PARAMS:ABSENCE_CODES", "PARAMS:ABSENCE_CODES:CREATE", "PARAMS:ABSENCE_CODES:UPDATE"
            });
        }

        if (rights.Contains(Rights.Processing))
        {
            defaults.AddRange(new[]
            {
                "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE", "PROC:SHIFTS:DELETE",
                "PROC:EMPLOYEE_PLANNING", "PROC:EMPLOYEE_PLANNING:CREATE", "PROC:EMPLOYEE_PLANNING:UPDATE", "PROC:EMPLOYEE_PLANNING:DELETE",
                "PROC:PUNCHES",
                "PROC:ANOMALIES", "PROC:ANOMALIES:DELETE",
                "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
                "PROC:WEEKLY_VALIDATION",
                "PROC:OVERTIME"
            });
        }

        if (rights.Contains(Rights.Other))
        {
            defaults.AddRange(new[]
            {
                "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES"
            });
        }

        if (rights.Contains(Rights.Traceability))
        {
            defaults.Add("TRACE:AUDIT");
        }

        if (rights.Contains(Rights.Reports))
        {
            defaults.Add("REPORTS:REPORTS");
        }

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

        return new UserDetailedPermissionsDto(userId, defaults.Distinct().ToList(), false);
    }

    public async Task SaveUserDetailedPermissionsAsync(decimal currentUserId, decimal targetUserId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        var caller = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(currentUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Utilisateur connecté introuvable.");

        var target = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur cible introuvable.");

        var callerRole = await _uow.Repository<T_ROLE>().GetByIdAsync(caller.IDROLE, cancellationToken);
        var callerIsAdmin = caller.IDROLE == 1 || (callerRole?.NOMROLE?.Contains("ADMIN", StringComparison.OrdinalIgnoreCase) ?? false);

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

        await _uow.SaveChangesAsync(cancellationToken);
    }
}

