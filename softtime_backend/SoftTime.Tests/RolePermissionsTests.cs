using System.Linq.Expressions;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;

namespace SoftTime.Tests;

public class RolePermissionsTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly FakeTokenService _tokens;
    private readonly IMapper _mapper;
    private readonly FakeEmailService _email;
    private readonly PasswordHasher<T_RESPONSABLE> _hasher;
    private readonly AuthService _auth;

    public RolePermissionsTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = new FakeTokenService();

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = new FakeEmailService();
        _hasher = new PasswordHasher<T_RESPONSABLE>();
        _auth = new AuthService(_uow, _tokens, _mapper, _email, _hasher);

        // Rôles initiaux
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Utilisateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 3, NOMROLE = "Manager" });

        // Admin connecté
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Super Admin" });
        // Non-admin connecté
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "Simple User" });
    }

    #region C1, C3 — Contrôles d'accès et validation

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_NonAdmin_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 2, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Seul un administrateur peut modifier les permissions d'un rôle.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_UnknownCaller_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 999, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CallerWithMissingRole_ThrowsInvalidOperationException()
    {
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 10, IDROLE = 999, LOGIN = "orphan", NOMRESPONSABLE = "Orphan" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 10, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_TargetRole1_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 1, new[] { "PARAMS:USERS" }));
        Assert.Equal("Les privilèges du rôle Administrateur ne sont pas modifiables.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_TargetRoleNotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 888, new[] { "PARAMS:USERS" }));
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_UnknownCode_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "INVALID:HACK:CODE" }));
        Assert.Contains("Codes de permission inconnus", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_DynamicSageCode_Succeeds()
    {
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "SAGE:DATABASE_TEST:READ" });

        var perms = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.True(perms.IsConfigured);
        Assert.Contains("SAGE:DATABASE_TEST:READ", perms.GrantedPermissions);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_AdminPaieRole_ThrowsInvalidOperationException()
    {
        // Rôle "Admin Paie" (IDROLE = 4 != 1)
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 4, NOMROLE = "Admin Paie" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 30, IDROLE = 4, LOGIN = "adminpaie", NOMRESPONSABLE = "Admin Paie User" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 30, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Seul un administrateur peut modifier les permissions d'un rôle.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_NullList_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, null!));
        Assert.Equal("La liste des permissions ne peut pas être nulle.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CaseInsensitiveDuplicates_SavedAsSingleUpperCaseRow()
    {
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "params:users", "PARAMS:USERS" });

        var perms = await _uow.GetRepo<T_ROLE_PERMISSION>().ListAsync(p => p.IDROLE == 3);
        Assert.Single(perms);
        Assert.Equal("PARAMS:USERS", perms[0].PERMISSION_CODE);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CodeWithSpaces_TrimmedAndAccepted()
    {
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "  params:users  " });

        var perms = await _uow.GetRepo<T_ROLE_PERMISSION>().ListAsync(p => p.IDROLE == 3);
        Assert.Single(perms);
        Assert.Equal("PARAMS:USERS", perms[0].PERMISSION_CODE);
    }

    #endregion

    #region C2 — Synchronisation exacte des portes

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_ExactGates_ParamsOnly_OnlyGate2Assigned()
    {
        // Donnons initialement toutes les portes au rôle 3
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Databases });
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Parameters });
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Processing });

        // Sauvegardons uniquement des permissions du module PARAMS
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PARAMS:USERS", "PARAMS:ROLES" });

        var droits = await _uow.GetRepo<T_DROIT_ROLE>().ListAsync(d => d.IDROLE == 3);
        Assert.Single(droits);
        Assert.Equal(Rights.Parameters, (int)droits[0].IDDROIT);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_ParamsUnchecked_GateRemoved()
    {
        // Rôle 3 a la porte Parameters
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Parameters });

        // Sauvegarde de permissions PROC uniquement
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PROC:SHIFTS" });

        var droits = await _uow.GetRepo<T_DROIT_ROLE>().ListAsync(d => d.IDROLE == 3);
        Assert.Single(droits);
        Assert.Equal(Rights.Processing, (int)droits[0].IDDROIT);
        Assert.DoesNotContain(droits, d => (int)d.IDDROIT == Rights.Parameters);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_EmptyList_SentinelInsertedAndAllGatesRemoved()
    {
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Parameters });

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, Array.Empty<string>());

        var rolePerms = await _uow.GetRepo<T_ROLE_PERMISSION>().ListAsync(p => p.IDROLE == 3);
        Assert.Single(rolePerms);
        Assert.Equal("__SOFTTIME_EMPTY_ROLE_PERMISSIONS__", rolePerms[0].PERMISSION_CODE);

        var droits = await _uow.GetRepo<T_DROIT_ROLE>().ListAsync(d => d.IDROLE == 3);
        Assert.Empty(droits);

        var res = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.True(res.IsConfigured);
        Assert.Empty(res.GrantedPermissions);
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_UnconfiguredRole_WithParametersGate_ReturnsParamsCodes_IsConfiguredFalse()
    {
        // Rôle 3 jamais configuré (aucune ligne T_ROLE_PERMISSION) mais ayant la porte Paramètres
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Parameters });

        var res = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.False(res.IsConfigured);
        Assert.Contains("PARAMS:USERS", res.GrantedPermissions);
        Assert.Contains("PARAMS:ROLES", res.GrantedPermissions);
        Assert.DoesNotContain("PROC:SHIFTS", res.GrantedPermissions);
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_ConfiguredRole_ReturnsConfiguredPermissions_IsConfiguredTrue()
    {
        // Rôle 3 ayant la porte Paramètres et configuré avec PROC:SHIFTS
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Parameters });
        _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 3, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });

        var res = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.True(res.IsConfigured);
        Assert.Single(res.GrantedPermissions);
        Assert.Contains("PROC:SHIFTS", res.GrantedPermissions);
    }

    #endregion

    #region C4 — Journalisation atomique (un seul SaveChangesAsync)

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_SingleSaveChangesAsync_AtomicJournal()
    {
        _uow.ResetSaveChangesCount();

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PARAMS:USERS" });

        Assert.Equal(1, _uow.SaveChangesCount);

        var audit = await _uow.GetRepo<T_AUDIT_DROIT>().ListAsync(a => a.TYPE_CIBLE == "ROLE" && a.ID_CIBLE == 3);
        Assert.Single(audit);
        Assert.Equal("UPDATE_ROLE_PERMS", audit[0].ACTION);
        Assert.Equal("admin", audit[0].LOGIN_OPERATEUR);
        Assert.Equal("PARAMS:USERS", audit[0].PERMS_APRES);
    }

    [Fact]
    public async Task EcrireAuditDroitAsync_LongLoginAndTargetName_SafelyTruncatedWithoutException()
    {
        var longLogin = new string('A', 150);
        var longRoleName = new string('B', 150);
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 5, NOMROLE = longRoleName });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 40, IDROLE = 1, LOGIN = longLogin, NOMRESPONSABLE = "Admin" });

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 40, targetRoleId: 5, new[] { "PARAMS:USERS" });

        var audit = await _uow.GetRepo<T_AUDIT_DROIT>().ListAsync(a => a.TYPE_CIBLE == "ROLE" && a.ID_CIBLE == 5);
        Assert.Single(audit);
        Assert.Equal(100, audit[0].LOGIN_OPERATEUR.Length);
        Assert.Equal(100, audit[0].NOM_CIBLE?.Length);
    }

    #endregion

    #region B2, C8 — Résolution des droits effectifs

    [Fact]
    public async Task GetEffectivePermissions_RoleConfigured_UserNotCustomized_InheritsRolePermissions()
    {
        // Rôle 3 configuré avec PROC:SHIFTS
        _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 3, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });

        // Utilisateur 3 avec rôle 3, sans T_USER_PERMISSION
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "user3" });

        var perms = await _auth.GetUserDetailedPermissionsAsync(3);
        Assert.False(perms.IsCustomized);
        Assert.Single(perms.GrantedPermissions);
        Assert.Contains("PROC:SHIFTS", perms.GrantedPermissions);
    }

    [Fact]
    public async Task GetEffectivePermissions_UserCustomized_OverridesRolePermissions()
    {
        // Rôle 3 configuré avec PROC:SHIFTS
        _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 3, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });

        // Utilisateur 3 avec rôle 3 et personnalisation T_USER_PERMISSION = PARAMS:USERS
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "user3" });
        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 3, PERMISSION_CODE = "PARAMS:USERS", IS_GRANTED = true });

        var perms = await _auth.GetUserDetailedPermissionsAsync(3);
        Assert.True(perms.IsCustomized);
        Assert.Single(perms.GrantedPermissions);
        Assert.Contains("PARAMS:USERS", perms.GrantedPermissions);
        Assert.DoesNotContain("PROC:SHIFTS", perms.GrantedPermissions);
    }

    [Fact]
    public async Task GetEffectivePermissions_RoleNeverConfigured_FallsBackToGates()
    {
        // Rôle 3 avec grande porte Traceability
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 3, IDDROIT = Rights.Traceability });

        // Utilisateur 3 avec rôle 3, sans aucune configuration de rôle ni d'utilisateur
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "user3" });

        var perms = await _auth.GetUserDetailedPermissionsAsync(3);
        Assert.False(perms.IsCustomized);
        Assert.Contains("TRACE:AUDIT", perms.GrantedPermissions);
    }

    #endregion

    #region B5, C1, C4 — Réinitialisation des permissions utilisateur

    [Fact]
    public async Task ResetUserPermissions_ClearsCustomizationAndReturnsToRolePermissions()
    {
        // Rôle 3 avec PROC:SHIFTS
        _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 3, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });

        // Utilisateur 3 personnalisé avec PARAMS:USERS
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "user3", NOMRESPONSABLE = "User 3" });
        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 3, PERMISSION_CODE = "PARAMS:USERS", IS_GRANTED = true });

        // Reset par l'admin
        await _auth.ResetUserDetailedPermissionsAsync(currentUserId: 1, targetUserId: 3);

        // T_USER_PERMISSION est vidé
        var userPerms = await _uow.GetRepo<T_USER_PERMISSION>().ListAsync(p => p.IDRESPONSABLE == 3);
        Assert.Empty(userPerms);

        // L'utilisateur n'est plus personnalisé et hérite du rôle
        var perms = await _auth.GetUserDetailedPermissionsAsync(3);
        Assert.False(perms.IsCustomized);
        Assert.Contains("PROC:SHIFTS", perms.GrantedPermissions);

        // Journal d'audit créé
        var audit = await _uow.GetRepo<T_AUDIT_DROIT>().ListAsync(a => a.TYPE_CIBLE == "USER" && a.ID_CIBLE == 3);
        Assert.Single(audit);
        Assert.Equal("RESET_USER_PERMS", audit[0].ACTION);
        Assert.Equal("PARAMS:USERS", audit[0].PERMS_AVANT);
    }

    [Fact]
    public async Task ResetUserPermissions_NonAdminSelf_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.ResetUserDetailedPermissionsAsync(currentUserId: 2, targetUserId: 2));
        Assert.Equal("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.", ex.Message);
    }

    #endregion

    #region C5 — Nettoyage à la suppression d'un rôle

    [Fact]
    public async Task DeleteRole_CleansRolePermissionsAndDroitsRole()
    {
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 4, NOMROLE = "RoleToDelete" });
        _uow.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 4, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });
        _uow.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDROLE = 4, IDDROIT = Rights.Processing });

        await _auth.DeleteRoleAsync(4);

        Assert.Null(await _uow.GetRepo<T_ROLE>().GetByIdAsync(4));
        var remainingPerms = await _uow.GetRepo<T_ROLE_PERMISSION>().ListAsync(p => p.IDROLE == 4);
        Assert.Empty(remainingPerms);
        var remainingDroits = await _uow.GetRepo<T_DROIT_ROLE>().ListAsync(d => d.IDROLE == 4);
        Assert.Empty(remainingDroits);
    }

    #endregion

    #region C6 — Changement de rôle et resetPermissions

    [Fact]
    public async Task SaveUserAsync_ResetPermissions_AppliedWhenRoleActuallyChanges()
    {
        // Utilisateur 3 avec rôle 2 et personnalisation
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 2, LOGIN = "user3", NOMRESPONSABLE = "User 3" });
        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 3, PERMISSION_CODE = "PARAMS:USERS", IS_GRANTED = true });

        // Changement de rôle vers 3 avec ResetPermissions = true
        var updateDto = new UserDto(3, 3, "User 3", "user3", null, null) { ResetPermissions = true };
        await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Permissions personnalisées purgées
        var userPerms = await _uow.GetRepo<T_USER_PERMISSION>().ListAsync(p => p.IDRESPONSABLE == 3);
        Assert.Empty(userPerms);

        // Audit de réinitialisation créé
        var audit = await _uow.GetRepo<T_AUDIT_DROIT>().ListAsync(a => a.TYPE_CIBLE == "USER" && a.ID_CIBLE == 3 && a.ACTION == "RESET_USER_PERMS");
        Assert.Single(audit);
    }

    [Fact]
    public async Task SaveUserAsync_ResetPermissions_IgnoredWhenRoleUnchanged()
    {
        // Utilisateur 3 avec rôle 2 et personnalisation
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 2, LOGIN = "user3", NOMRESPONSABLE = "User 3" });
        _uow.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 3, PERMISSION_CODE = "PARAMS:USERS", IS_GRANTED = true });

        // Même rôle 2 avec ResetPermissions = true
        var updateDto = new UserDto(3, 2, "User 3 Modif Nom", "user3", null, null) { ResetPermissions = true };
        await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Permissions personnalisées NON purgées (car le rôle n'a pas changé)
        var userPerms = await _uow.GetRepo<T_USER_PERMISSION>().ListAsync(p => p.IDRESPONSABLE == 3);
        Assert.Single(userPerms);

        // Aucun audit RESET_USER_PERMS
        var audit = await _uow.GetRepo<T_AUDIT_DROIT>().ListAsync(a => a.TYPE_CIBLE == "USER" && a.ID_CIBLE == 3 && a.ACTION == "RESET_USER_PERMS");
        Assert.Empty(audit);
    }

    #endregion

    #region In-Memory Test Doubles

    private sealed class InMemoryRepository<T> : IRepository<T> where T : class
    {
        public List<T> Items { get; } = new();

        public IQueryable<T> Query() => Items.AsQueryable();

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            var decId = Convert.ToDecimal(id);
            var match = Items.FirstOrDefault(i =>
            {
                if (i is T_RESPONSABLE r && r.IDRESPONSABLE == decId) return true;
                if (i is T_ROLE ro && ro.IDROLE == decId) return true;
                if (i is T_ROLE_PERMISSION rp && rp.ID == (int)decId) return true;
                if (i is T_AUDIT_DROIT ad && ad.ID == (int)decId) return true;
                if (i is T_USER_PERMISSION up && up.ID == (int)decId) return true;
                return false;
            });
            return Task.FromResult(match);
        }

        public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            return Task.FromResult(Items.Where(func).ToList());
        }

        public Task<List<T>> ListLatestAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<T, DateTime?>> orderByDescending,
            int take,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            var orderFunc = orderByDescending.Compile();
            return Task.FromResult(Items.Where(func).OrderByDescending(orderFunc).Take(take).ToList());
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is T_RESPONSABLE r && r.IDRESPONSABLE == 0)
            {
                r.IDRESPONSABLE = Items.Count == 0 ? 1 : Items.OfType<T_RESPONSABLE>().Max(x => x.IDRESPONSABLE) + 1;
            }
            if (entity is T_ROLE ro && ro.IDROLE == 0)
            {
                ro.IDROLE = Items.Count == 0 ? 1 : Items.OfType<T_ROLE>().Max(x => x.IDROLE) + 1;
            }
            if (entity is T_ROLE_PERMISSION rp && rp.ID == 0)
            {
                rp.ID = Items.Count == 0 ? 1 : Items.OfType<T_ROLE_PERMISSION>().Max(x => x.ID) + 1;
            }
            if (entity is T_AUDIT_DROIT ad && ad.ID == 0)
            {
                ad.ID = Items.Count == 0 ? 1 : Items.OfType<T_AUDIT_DROIT>().Max(x => x.ID) + 1;
            }
            if (entity is T_USER_PERMISSION up && up.ID == 0)
            {
                up.ID = Items.Count == 0 ? 1 : Items.OfType<T_USER_PERMISSION>().Max(x => x.ID) + 1;
            }
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            foreach (var e in entities)
                AddAsync(e, cancellationToken);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Remove(T entity) => Items.Remove(entity);
        public void RemoveRange(IEnumerable<T> entities)
        {
            foreach (var e in entities.ToList())
                Items.Remove(e);
        }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repos = new();
        public int SaveChangesCount { get; private set; }

        public void ResetSaveChangesCount() => SaveChangesCount = 0;

        public InMemoryRepository<T> GetRepo<T>() where T : class
        {
            if (!_repos.TryGetValue(typeof(T), out var r))
            {
                r = new InMemoryRepository<T>();
                _repos[typeof(T)] = r;
            }
            return (InMemoryRepository<T>)r;
        }

        public IRepository<T> Repository<T>() where T : class => GetRepo<T>();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights)
            => $"FAKE_JWT_TOKEN_{userId}_{login}";
    }

    private sealed class FakeEmailService : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    #endregion
}
