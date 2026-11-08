using System.Linq.Expressions;

using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
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
    private readonly PasswordHasher<Responsable> _hasher;
    private readonly AuthServices _auth;

    public RolePermissionsTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = new FakeTokenService();

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = new FakeEmailService();
        _hasher = new PasswordHasher<Responsable>();
        _auth = new AuthServices(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

        // Rôles initiaux
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Manager" });

        // Admin connecté
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Super Admin" });
        // Non-admin connecté
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 2, Login = "user", NomResponsable = "Simple User" });
    }

    #region C1, C3 — Contrôles d'accès et validation

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_NonAdmin_ThrowsDomainException()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 2, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Seul un administrateur peut modifier les permissions d'un rôle.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_UnknownCaller_ThrowsDomainException()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 999, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CallerWithMissingRole_ThrowsDomainException()
    {
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 10, IdRole = 999, Login = "orphan", NomResponsable = "Orphan" });

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 10, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_TargetRole1_ThrowsDomainException()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 1, new[] { "PARAMS:USERS" }));
        Assert.Equal("Les privilèges du rôle Administrateur ne sont pas modifiables.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_TargetRoleNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 888, new[] { "PARAMS:USERS" }));
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_UnknownCode_ThrowsDomainException()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
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
    public async Task SaveRoleDetailedPermissionsAsync_AdminPaieRole_ThrowsDomainException()
    {
        // Rôle "Admin Paie" (IDROLE = 4 != 1)
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 4, NomRole = "Admin Paie" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 30, IdRole = 4, Login = "adminpaie", NomResponsable = "Admin Paie User" });

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 30, targetRoleId: 3, new[] { "PARAMS:USERS" }));
        Assert.Equal("Seul un administrateur peut modifier les permissions d'un rôle.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_NullList_ThrowsDomainException()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, null!));
        Assert.Equal("La liste des permissions ne peut pas être nulle.", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CaseInsensitiveDuplicates_SavedAsSingleUpperCaseRow()
    {
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "params:users", "PARAMS:USERS" });

        var perms = await _uow.GetRepo<RolePermission>().ListAsync(p => p.IdRole == 3);
        Assert.Single(perms);
        Assert.Equal("PARAMS:USERS", perms[0].PermissionCode);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CodeWithSpaces_TrimmedAndAccepted()
    {
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "  params:users  " });

        var perms = await _uow.GetRepo<RolePermission>().ListAsync(p => p.IdRole == 3);
        Assert.Single(perms);
        Assert.Equal("PARAMS:USERS", perms[0].PermissionCode);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CodeLongerThan128Chars_ThrowsDomainException()
    {
        var longCode = "SAGE:" + new string('A', 124);
        Assert.Equal(129, longCode.Length);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { longCode }));

        Assert.Contains("128", ex.Message);
        Assert.Contains("caractères", ex.Message);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CodeExactly128Chars_IsAccepted()
    {
        var validCode = "SAGE:" + new string('A', 123);
        Assert.Equal(128, validCode.Length);

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { validCode });

        var perms = await _uow.GetRepo<RolePermission>().ListAsync(p => p.IdRole == 3);
        Assert.Single(perms);
        Assert.Equal(validCode, perms[0].PermissionCode);
    }

    #endregion

    #region C2 — Synchronisation exacte des portes

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_ExactGates_ParamsOnly_OnlyGate2Assigned()
    {
        // Donnons initialement toutes les portes au rôle 3
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Databases });
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Processing });

        // Sauvegardons uniquement des permissions du module PARAMS
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PARAMS:USERS", "PARAMS:ROLES" });

        var droits = await _uow.GetRepo<DroitRole>().ListAsync(d => d.IdRole == 3);
        Assert.Single(droits);
        Assert.Equal(Rights.Parameters, (int)droits[0].IdDroit);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_ParamsUnchecked_GateRemoved()
    {
        // Rôle 3 a la porte Parameters
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });

        // Sauvegarde de permissions PROC uniquement
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PROC:SHIFTS" });

        var droits = await _uow.GetRepo<DroitRole>().ListAsync(d => d.IdRole == 3);
        Assert.Single(droits);
        Assert.Equal(Rights.Processing, (int)droits[0].IdDroit);
        Assert.DoesNotContain(droits, d => (int)d.IdDroit == Rights.Parameters);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_EmptyList_SentinelInsertedAndAllGatesRemoved()
    {
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, Array.Empty<string>());

        var rolePerms = await _uow.GetRepo<RolePermission>().ListAsync(p => p.IdRole == 3);
        Assert.Single(rolePerms);
        Assert.Equal("__SOFTTIME_EMPTY_ROLE_PERMISSIONS__", rolePerms[0].PermissionCode);

        var droits = await _uow.GetRepo<DroitRole>().ListAsync(d => d.IdRole == 3);
        Assert.Empty(droits);

        var res = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.True(res.IsConfigured);
        Assert.Empty(res.GrantedPermissions);
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_UnconfiguredRole_WithParametersGate_ReturnsParamsCodes_IsConfiguredFalse()
    {
        // Rôle 3 jamais configuré (aucune ligne T_ROLE_PERMISSION) mais ayant la porte Paramètres
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });

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
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });
        _uow.GetRepo<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        var res = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.True(res.IsConfigured);
        Assert.Single(res.GrantedPermissions);
        Assert.Contains("PROC:SHIFTS", res.GrantedPermissions);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_RoundTrip_ParametersGate_GatesUnchanged()
    {
        // Rôle 3 avec la porte Paramètres, jamais configuré
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });

        var initial = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.False(initial.IsConfigured);
        Assert.NotEmpty(initial.GrantedPermissions);

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, initial.GrantedPermissions);

        var portesApres = await _uow.GetRepo<DroitRole>().ListAsync(d => d.IdRole == 3);
        Assert.Single(portesApres);
        Assert.Equal(Rights.Parameters, (int)portesApres[0].IdDroit);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_RoundTrip_DatabasesProcessingReportsGates_GatesUnchanged()
    {
        // Rôle 3 avec les portes Databases + Processing + Reports, jamais configuré
        // Une base SAGE et une base pointeuse existent : leurs droits de lecture portent la porte Bases (depuis 4b-10 les droits
        // par défaut ne contiennent plus la configuration SAGE:CREATE, qui portait cette porte même sans aucune base).
        _uow.GetRepo<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "BASE1" });
        _uow.GetRepo<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTG" });
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Databases });
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Processing });
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Reports });

        var initial = await _auth.GetRoleDetailedPermissionsAsync(3);
        Assert.False(initial.IsConfigured);
        Assert.NotEmpty(initial.GrantedPermissions);

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, initial.GrantedPermissions);

        var portesApres = await _uow.GetRepo<DroitRole>().ListAsync(d => d.IdRole == 3);
        var gateIds = portesApres.Select(p => (int)p.IdDroit).OrderBy(x => x).ToList();
        var expectedGates = new[] { Rights.Databases, Rights.Processing, Rights.Reports }.OrderBy(x => x).ToList();
        Assert.Equal(expectedGates, gateIds);
    }

    #endregion

    #region C4 — Journalisation atomique (un seul SaveChangesAsync)

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_SingleSaveChangesAsync_AtomicJournal()
    {
        _uow.ResetSaveChangesCount();

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PARAMS:USERS" });

        Assert.Equal(1, _uow.SaveChangesCount);

        var audit = await _uow.GetRepo<AuditDroit>().ListAsync(a => a.TypeCible == "ROLE" && a.IdCible == 3);
        Assert.Single(audit);
        Assert.Equal("UPDATE_ROLE_PERMS", audit[0].Action);
        Assert.Equal("admin", audit[0].LoginOperateur);
        Assert.Equal("PARAMS:USERS", audit[0].PermsApres);
    }

    [Fact]
    public async Task EcrireAuditDroitAsync_LongLoginAndTargetName_SafelyTruncatedWithoutException()
    {
        var longLogin = new string('A', 150);
        var longRoleName = new string('B', 150);
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 5, NomRole = longRoleName });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 40, IdRole = 1, Login = longLogin, NomResponsable = "Admin" });

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 40, targetRoleId: 5, new[] { "PARAMS:USERS" });

        var audit = await _uow.GetRepo<AuditDroit>().ListAsync(a => a.TypeCible == "ROLE" && a.IdCible == 5);
        Assert.Single(audit);
        Assert.Equal(100, audit[0].LoginOperateur.Length);
        Assert.Equal(100, audit[0].NomCible?.Length);
    }

    [Fact]
    public async Task SaveUserAsync_RoleChangeWithConfirmedClear_SingleSaveChangesAsync_AuditResetPresent()
    {
        // Utilisateur 2 (actuellement IDROLE = 2) a des permissions personnalisées
        _uow.GetRepo<UserPermission>().Items.Add(new UserPermission
        {
            Id = 1,
            IdResponsable = 2,
            PermissionCode = "PARAMS:USERS",
            IsGranted = true
        });

        var dto = new UserDto(2, 3, "Simple User", "user", null, null)
        {
            ConfirmClearAdjustments = true
        };

        _uow.ResetSaveChangesCount();

        await _auth.SaveUserAsync(currentUserId: 1, dto);

        // M3 : un seul SaveChangesAsync
        Assert.Equal(1, _uow.SaveChangesCount);

        // L'audit RESET_USER_PERMS est bien présent
        var audits = await _uow.GetRepo<AuditDroit>().ListAsync(
            a => a.TypeCible == "USER" && a.IdCible == 2 && a.Action == "RESET_USER_PERMS");
        Assert.Single(audits);
        Assert.Equal("admin", audits[0].LoginOperateur);
        Assert.Equal("PARAMS:USERS", audits[0].PermsAvant);
        Assert.Null(audits[0].PermsApres);

        // Vérification que les permissions ont été nettoyées
        var permsAfter = await _uow.GetRepo<UserPermission>().ListAsync(p => p.IdResponsable == 2);
        Assert.Empty(permsAfter);

        // Vérification que le rôle a bien été mis à jour
        var userAfter = await _uow.GetRepo<Responsable>().GetByIdAsync(2);
        Assert.Equal(3, userAfter!.IdRole);
    }

    #endregion

    #region B2, C8 — Résolution des droits effectifs

    [Fact]
    public async Task GetEffectivePermissions_RoleConfigured_UserNotCustomized_InheritsRolePermissions()
    {
        // Rôle 3 configuré avec PROC:SHIFTS
        _uow.GetRepo<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        // Utilisateur 3 avec rôle 3, sans T_USER_PERMISSION
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3" });

        var perms = await _auth.GetUserDetailedPermissionsAsync(3);
        Assert.False(perms.IsCustomized);
        Assert.Single(perms.GrantedPermissions);
        Assert.Contains("PROC:SHIFTS", perms.GrantedPermissions);
    }

    [Fact]
    public async Task GetEffectivePermissions_UserCustomized_OverridesRolePermissions()
    {
        // Rôle 3 configuré avec PROC:SHIFTS
        _uow.GetRepo<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        // Utilisateur 3 avec rôle 3 et personnalisation T_USER_PERMISSION = PARAMS:USERS
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3" });
        _uow.GetRepo<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true });

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
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Traceability });

        // Utilisateur 3 avec rôle 3, sans aucune configuration de rôle ni d'utilisateur
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3" });

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
        _uow.GetRepo<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        // Utilisateur 3 personnalisé avec PARAMS:USERS
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
        _uow.GetRepo<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true });

        // Reset par l'admin
        await _auth.ResetUserDetailedPermissionsAsync(currentUserId: 1, targetUserId: 3);

        // T_USER_PERMISSION est vidé
        var userPerms = await _uow.GetRepo<UserPermission>().ListAsync(p => p.IdResponsable == 3);
        Assert.Empty(userPerms);

        // L'utilisateur n'est plus personnalisé et hérite du rôle
        var perms = await _auth.GetUserDetailedPermissionsAsync(3);
        Assert.False(perms.IsCustomized);
        Assert.Contains("PROC:SHIFTS", perms.GrantedPermissions);

        // Journal d'audit créé
        var audit = await _uow.GetRepo<AuditDroit>().ListAsync(a => a.TypeCible == "USER" && a.IdCible == 3);
        Assert.Single(audit);
        Assert.Equal("RESET_USER_PERMS", audit[0].Action);
        Assert.Equal("PARAMS:USERS", audit[0].PermsAvant);
    }

    [Fact]
    public async Task ResetUserPermissions_NonAdminSelf_ThrowsDomainException()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.ResetUserDetailedPermissionsAsync(currentUserId: 2, targetUserId: 2));
        Assert.Equal("Un utilisateur ne peut pas modifier ses propres privilèges sans droit d'administration.", ex.Message);
    }

    #endregion

    #region C5 — Nettoyage à la suppression d'un rôle

    [Fact]
    public async Task DeleteRole_CleansRolePermissionsAndDroitsRole()
    {
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 4, NomRole = "RoleToDelete" });
        _uow.GetRepo<RolePermission>().Items.Add(new RolePermission { IdRole = 4, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        _uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = 4, IdDroit = Rights.Processing });

        await _auth.DeleteRoleAsync(4);

        Assert.Null(await _uow.GetRepo<Role>().GetByIdAsync(4));
        var remainingPerms = await _uow.GetRepo<RolePermission>().ListAsync(p => p.IdRole == 4);
        Assert.Empty(remainingPerms);
        var remainingDroits = await _uow.GetRepo<DroitRole>().ListAsync(d => d.IdRole == 4);
        Assert.Empty(remainingDroits);
    }

    #endregion

    #region C6 — Changement de rôle et resetPermissions

    [Fact]
    public async Task SaveUserAsync_ConfirmClearAdjustments_AppliedWhenRoleActuallyChanges()
    {
        // Utilisateur 3 avec rôle 2 et personnalisation
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 2, Login = "user3", NomResponsable = "User 3" });
        _uow.GetRepo<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true });

        // Changement de rôle vers 3 avec ConfirmClearAdjustments = true
        var updateDto = new UserDto(3, 3, "User 3", "user3", null, null) { ConfirmClearAdjustments = true };
        await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Permissions personnalisées purgées
        var userPerms = await _uow.GetRepo<UserPermission>().ListAsync(p => p.IdResponsable == 3);
        Assert.Empty(userPerms);

        // Audit de réinitialisation créé
        var audit = await _uow.GetRepo<AuditDroit>().ListAsync(a => a.TypeCible == "USER" && a.IdCible == 3 && a.Action == "RESET_USER_PERMS");
        Assert.Single(audit);
    }

    [Fact]
    public async Task SaveUserAsync_ConfirmClearAdjustments_IgnoredWhenRoleUnchanged()
    {
        // Utilisateur 3 avec rôle 2 et personnalisation
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 2, Login = "user3", NomResponsable = "User 3" });
        _uow.GetRepo<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true });

        // Même rôle 2 avec ConfirmClearAdjustments = true
        var updateDto = new UserDto(3, 2, "User 3 Modif Nom", "user3", null, null) { ConfirmClearAdjustments = true };
        await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Permissions personnalisées NON purgées (car le rôle n'a pas changé)
        var userPerms = await _uow.GetRepo<UserPermission>().ListAsync(p => p.IdResponsable == 3);
        Assert.Single(userPerms);

        // Aucun audit RESET_USER_PERMS
        var audit = await _uow.GetRepo<AuditDroit>().ListAsync(a => a.TypeCible == "USER" && a.IdCible == 3 && a.Action == "RESET_USER_PERMS");
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
                if (i is Responsable r && r.IdResponsable == decId) return true;
                if (i is Role ro && ro.IdRole == decId) return true;
                if (i is RolePermission rp && rp.Id == (int)decId) return true;
                if (i is AuditDroit ad && ad.Id == (int)decId) return true;
                if (i is UserPermission up && up.Id == (int)decId) return true;
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
            if (entity is Responsable r && r.IdResponsable == 0)
            {
                r.IdResponsable = Items.Count == 0 ? 1 : Items.OfType<Responsable>().Max(x => x.IdResponsable) + 1;
            }
            if (entity is Role ro && ro.IdRole == 0)
            {
                ro.IdRole = Items.Count == 0 ? 1 : Items.OfType<Role>().Max(x => x.IdRole) + 1;
            }
            if (entity is RolePermission rp && rp.Id == 0)
            {
                rp.Id = Items.Count == 0 ? 1 : Items.OfType<RolePermission>().Max(x => x.Id) + 1;
            }
            if (entity is AuditDroit ad && ad.Id == 0)
            {
                ad.Id = Items.Count == 0 ? 1 : Items.OfType<AuditDroit>().Max(x => x.Id) + 1;
            }
            if (entity is UserPermission up && up.Id == 0)
            {
                up.Id = Items.Count == 0 ? 1 : Items.OfType<UserPermission>().Max(x => x.Id) + 1;
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
        public string CreateToken(TokenSubject subject)
            => $"FAKE_JWT_TOKEN_{subject.UserId}_{subject.Login}";
    }

    private sealed class FakeEmailService : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    #endregion
}
