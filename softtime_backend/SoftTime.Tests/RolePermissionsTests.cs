using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class RolePermissionsTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;
    private readonly IEmailService _email;
    private readonly PasswordHasher<Responsable> _hasher;
    private readonly AuthServices _auth;

    public RolePermissionsTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = ServiceMocks.TokenService().Object;

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = ServiceMocks.EmailService().Object;
        _hasher = new PasswordHasher<Responsable>();
        _auth = new AuthServices(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

        // Rôles initiaux
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Manager" });

        // Admin connecté
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Super Admin" });
        // Non-admin connecté
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 2, Login = "user", NomResponsable = "Simple User" });
    }

    #region C1, C3 — Contrôles d'accès et validation

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_NonAdmin_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 2, targetRoleId: 3, new[] { "PARAMS:USERS" });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Seul un administrateur peut modifier les permissions d'un rôle.");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_UnknownCaller_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 999, targetRoleId: 3, new[] { "PARAMS:USERS" });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Utilisateur appelant introuvable.");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CallerWithMissingRole_ThrowsDomainException()
    {
        // Arrange
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 10, IdRole = 999, Login = "orphan", NomResponsable = "Orphan" });

        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 10, targetRoleId: 3, new[] { "PARAMS:USERS" });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Utilisateur appelant introuvable.");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_TargetRole1_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 1, new[] { "PARAMS:USERS" });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Les privilèges du rôle Administrateur ne sont pas modifiables.");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_TargetRoleNotFound_ThrowsNotFoundException()
    {
        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 888, new[] { "PARAMS:USERS" });

        // Assert
        await act.Should().ThrowExactlyAsync<NotFoundException>();
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_UnknownCode_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "INVALID:HACK:CODE" });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Contain("Codes de permission inconnus");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_DynamicSageCode_Succeeds()
    {
        // Arrange
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "SAGE:DATABASE_TEST:READ" });

        // Act
        var perms = await _auth.GetRoleDetailedPermissionsAsync(3);

        // Assert
        perms.IsConfigured.Should().BeTrue();
        perms.GrantedPermissions.Should().Contain("SAGE:DATABASE_TEST:READ");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_AdminPaieRole_ThrowsDomainException()
    {
        // Arrange
        // Rôle "Admin Paie" (IDROLE = 4 != 1)
        _uow.Get<Role>().Items.Add(new Role { IdRole = 4, NomRole = "Admin Paie" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 30, IdRole = 4, Login = "adminpaie", NomResponsable = "Admin Paie User" });

        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 30, targetRoleId: 3, new[] { "PARAMS:USERS" });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Seul un administrateur peut modifier les permissions d'un rôle.");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_NullList_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, null!);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("La liste des permissions ne peut pas être nulle.");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CaseInsensitiveDuplicates_SavedAsSingleUpperCaseRow()
    {
        // Arrange
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "params:users", "PARAMS:USERS" });

        // Act
        var perms = await _uow.Get<RolePermission>().ListAsync(p => p.IdRole == 3);

        // Assert
        perms.Should().ContainSingle();
        perms[0].PermissionCode.Should().Be("PARAMS:USERS");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CodeWithSpaces_TrimmedAndAccepted()
    {
        // Arrange
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "  params:users  " });

        // Act
        var perms = await _uow.Get<RolePermission>().ListAsync(p => p.IdRole == 3);

        // Assert
        perms.Should().ContainSingle();
        perms[0].PermissionCode.Should().Be("PARAMS:USERS");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CodeLongerThan128Chars_ThrowsDomainException()
    {
        var longCode = "SAGE:" + new string('A', 124);
        longCode.Length.Should().Be(129);

        var ex = (await FluentActions.Awaiting(() =>
            _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { longCode })).Should().ThrowExactlyAsync<DomainException>()).Which;

        ex.Message.Should().Contain("128");
        ex.Message.Should().Contain("caractères");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CodeExactly128Chars_IsAccepted()
    {
        // Motif SAGE:<base>:<action> : 5 + 118 + 5 = 128 caractères.
        var validCode = "SAGE:" + new string('A', 118) + ":READ";
        validCode.Length.Should().Be(128);

        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { validCode });

        var perms = await _uow.Get<RolePermission>().ListAsync(p => p.IdRole == 3);
        perms.Should().ContainSingle();
        perms[0].PermissionCode.Should().Be(validCode);
    }

    public static IEnumerable<object[]> CatalogCodes()
        => PermissionCatalog.KnownStatic.Select(code => new object[] { code });

    [Theory]
    [MemberData(nameof(CatalogCodes))]
    public async Task SaveRoleDetailedPermissionsAsync_EveryCatalogCode_IsAccepted(string code)
    {
        // Act
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { code });

        // Assert
        var perms = await _uow.Get<RolePermission>().ListAsync(p => p.IdRole == 3);
        perms.Should().ContainSingle().Which.PermissionCode.Should().Be(code);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_EmployeeRoleWithSelfRead_IsSavedAndReadBack()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 6, NomRole = "Salarié" });

        // Act
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 6, new[] { PermissionCatalog.SelfRead });

        // Assert
        var perms = await _auth.GetRoleDetailedPermissionsAsync(6);
        perms.IsConfigured.Should().BeTrue();
        perms.GrantedPermissions.Should().Equal(PermissionCatalog.SelfRead);
        (await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 6)).Should().BeEmpty("SELF:READ n'ouvre aucune porte");
    }

    #endregion

    #region C2 — Synchronisation exacte des portes

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_ExactGates_ParamsOnly_OnlyGate2Assigned()
    {
        // Arrange
        // Donnons initialement toutes les portes au rôle 3
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Databases });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Processing });
        // Sauvegardons uniquement des permissions du module PARAMS
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PARAMS:USERS", "PARAMS:ROLES" });

        // Act
        var droits = await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 3);

        // Assert
        droits.Should().ContainSingle();
        ((int)droits[0].IdDroit).Should().Be(Rights.Parameters);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_ParamsUnchecked_GateRemoved()
    {
        // Arrange
        // Rôle 3 a la porte Parameters
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });
        // Sauvegarde de permissions PROC uniquement
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PROC:SHIFTS" });

        // Act
        var droits = await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 3);

        // Assert
        droits.Should().ContainSingle();
        ((int)droits[0].IdDroit).Should().Be(Rights.Processing);
        droits.Should().NotContain(d => (int)d.IdDroit == Rights.Parameters);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_EmptyList_SentinelInsertedAndAllGatesRemoved()
    {
        // Arrange
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, Array.Empty<string>());

        // Act
        var rolePerms = await _uow.Get<RolePermission>().ListAsync(p => p.IdRole == 3);

        // Assert
        rolePerms.Should().ContainSingle();
        rolePerms[0].PermissionCode.Should().Be("__SOFTTIME_EMPTY_ROLE_PERMISSIONS__");
        var droits = await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 3);
        droits.Should().BeEmpty();
        var res = await _auth.GetRoleDetailedPermissionsAsync(3);
        res.IsConfigured.Should().BeTrue();
        res.GrantedPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_UnconfiguredRole_WithParametersGate_ReturnsParamsCodes_IsConfiguredFalse()
    {
        // Arrange
        // Rôle 3 jamais configuré (aucune ligne T_ROLE_PERMISSION) mais ayant la porte Paramètres
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });

        // Act
        var res = await _auth.GetRoleDetailedPermissionsAsync(3);

        // Assert
        res.IsConfigured.Should().BeFalse();
        res.GrantedPermissions.Should().Contain("PARAMS:USERS");
        res.GrantedPermissions.Should().Contain("PARAMS:ROLES");
        res.GrantedPermissions.Should().NotContain("PROC:SHIFTS");
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_ConfiguredRole_ReturnsConfiguredPermissions_IsConfiguredTrue()
    {
        // Arrange
        // Rôle 3 ayant la porte Paramètres et configuré avec PROC:SHIFTS
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });

        // Act
        var res = await _auth.GetRoleDetailedPermissionsAsync(3);

        // Assert
        res.IsConfigured.Should().BeTrue();
        res.GrantedPermissions.Should().ContainSingle();
        res.GrantedPermissions.Should().Contain("PROC:SHIFTS");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_RoundTrip_ParametersGate_GatesUnchanged()
    {
        // Arrange
        // Rôle 3 avec la porte Paramètres, jamais configuré
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Parameters });

        // Act
        var initial = await _auth.GetRoleDetailedPermissionsAsync(3);

        // Assert
        initial.IsConfigured.Should().BeFalse();
        initial.GrantedPermissions.Should().NotBeEmpty();
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, initial.GrantedPermissions);
        var portesApres = await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 3);
        portesApres.Should().ContainSingle();
        ((int)portesApres[0].IdDroit).Should().Be(Rights.Parameters);
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_RoundTrip_DatabasesProcessingReportsGates_GatesUnchanged()
    {
        // Arrange
        // Rôle 3 avec les portes Databases + Processing + Reports, jamais configuré
        // Une base SAGE et une base pointeuse existent : leurs droits de lecture portent la porte Bases (depuis 4b-10 les droits
        // par défaut ne contiennent plus la configuration SAGE:CREATE, qui portait cette porte même sans aucune base).
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "BASE1" });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTG" });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Databases });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Processing });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Reports });

        // Act
        var initial = await _auth.GetRoleDetailedPermissionsAsync(3);

        // Assert
        initial.IsConfigured.Should().BeFalse();
        initial.GrantedPermissions.Should().NotBeEmpty();
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, initial.GrantedPermissions);
        var portesApres = await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 3);
        var gateIds = portesApres.Select(p => (int)p.IdDroit).OrderBy(x => x).ToList();
        var expectedGates = new[] { Rights.Databases, Rights.Processing, Rights.Reports }.OrderBy(x => x).ToList();
        gateIds.Should().Equal(expectedGates);
    }

    #endregion

    #region C4 — Journalisation atomique (un seul SaveChangesAsync)

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_SingleSaveChangesAsync_AtomicJournal()
    {
        // Arrange
        _uow.ResetSaveChangesCount();

        // Act
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 1, targetRoleId: 3, new[] { "PARAMS:USERS" });

        // Assert
        _uow.SaveChangesCount.Should().Be(1);
        var audit = await _uow.Get<AuditDroit>().ListAsync(a => a.TypeCible == "ROLE" && a.IdCible == 3);
        audit.Should().ContainSingle();
        audit[0].Action.Should().Be("UPDATE_ROLE_PERMS");
        audit[0].LoginOperateur.Should().Be("admin");
        audit[0].PermsApres.Should().Be("PARAMS:USERS");
    }

    [Fact]
    public async Task EcrireAuditDroitAsync_LongLoginAndTargetName_SafelyTruncatedWithoutException()
    {
        // Arrange
        var longLogin = new string('A', 150);
        var longRoleName = new string('B', 150);
        _uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = longRoleName });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 40, IdRole = 1, Login = longLogin, NomResponsable = "Admin" });
        await _auth.SaveRoleDetailedPermissionsAsync(currentUserId: 40, targetRoleId: 5, new[] { "PARAMS:USERS" });

        // Act
        var audit = await _uow.Get<AuditDroit>().ListAsync(a => a.TypeCible == "ROLE" && a.IdCible == 5);

        // Assert
        audit.Should().ContainSingle();
        audit[0].LoginOperateur.Length.Should().Be(100);
        (audit[0].NomCible?.Length).Should().Be(100);
    }

    [Fact]
    public async Task SaveUserAsync_RoleChangeWithConfirmedClear_SingleSaveChangesAsync_AuditResetPresent()
    {
        // Arrange
        // Utilisateur 2 (actuellement IDROLE = 2) a des permissions personnalisées
        _uow.Get<UserPermission>().Items.Add(new UserPermission
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

        // Act
        await _auth.SaveUserAsync(currentUserId: 1, dto);

        // Assert
        // M3 : un seul SaveChangesAsync
        _uow.SaveChangesCount.Should().Be(1);
        // L'audit RESET_USER_PERMS est bien présent
        var audits = await _uow.Get<AuditDroit>().ListAsync(
            a => a.TypeCible == "USER" && a.IdCible == 2 && a.Action == "RESET_USER_PERMS");
        audits.Should().ContainSingle();
        audits[0].LoginOperateur.Should().Be("admin");
        audits[0].PermsAvant.Should().Be("PARAMS:USERS");
        audits[0].PermsApres.Should().BeNull();
        // Vérification que les permissions ont été nettoyées
        var permsAfter = await _uow.Get<UserPermission>().ListAsync(p => p.IdResponsable == 2);
        permsAfter.Should().BeEmpty();
        // Vérification que le rôle a bien été mis à jour
        var userAfter = await _uow.Get<Responsable>().GetByIdAsync(2);
        userAfter!.IdRole.Should().Be(3);
    }

    #endregion

    #region B2, C8 — Résolution des droits effectifs

    [Fact]
    public async Task GetEffectivePermissions_RoleConfigured_UserNotCustomized_InheritsRolePermissions()
    {
        // Arrange
        // Rôle 3 configuré avec PROC:SHIFTS
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        // Utilisateur 3 avec rôle 3, sans T_USER_PERMISSION
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3" });

        // Act
        var perms = await _auth.GetUserDetailedPermissionsAsync(3);

        // Assert
        perms.IsCustomized.Should().BeFalse();
        perms.GrantedPermissions.Should().ContainSingle();
        perms.GrantedPermissions.Should().Contain("PROC:SHIFTS");
    }

    [Fact]
    public async Task GetEffectivePermissions_UserCustomized_OverridesRolePermissions()
    {
        // Arrange
        // Rôle 3 configuré avec PROC:SHIFTS
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        // Utilisateur 3 avec rôle 3 et personnalisation T_USER_PERMISSION = PARAMS:USERS (copie complète d'avant la
        // migration 006, convertie en écarts comme au démarrage de l'API)
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3" });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true, IsDeviation = false });
        await SoftTime.Tests.Http.TestRights.ConvertLegacyCopiesAsync(_uow);

        // Act
        var perms = await _auth.GetUserDetailedPermissionsAsync(3);

        // Assert
        perms.IsCustomized.Should().BeTrue();
        perms.GrantedPermissions.Should().ContainSingle();
        perms.GrantedPermissions.Should().Contain("PARAMS:USERS");
        perms.GrantedPermissions.Should().NotContain("PROC:SHIFTS");
    }

    [Fact]
    public async Task GetEffectivePermissions_RoleNeverConfigured_FallsBackToGates()
    {
        // Arrange
        // Rôle 3 avec grande porte Traceability
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = Rights.Traceability });
        // Utilisateur 3 avec rôle 3, sans aucune configuration de rôle ni d'utilisateur
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3" });

        // Act
        var perms = await _auth.GetUserDetailedPermissionsAsync(3);

        // Assert
        perms.IsCustomized.Should().BeFalse();
        perms.GrantedPermissions.Should().Contain("TRACE:AUDIT");
    }

    #endregion

    #region Lot 2 — Validation des droits d'un utilisateur (même validateur que les rôles)

    private List<string> UserCodes(decimal userId)
        => _uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == userId).Select(p => p.PermissionCode).ToList();

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_ValidCodes_AreNormalizedAndSaved()
    {
        // Act
        await _auth.SaveUserDetailedPermissionsAsync(1, 2, new[] { "proc:shifts", " sage:Base_1:read ", "PROC:SHIFTS", PermissionCatalog.SelfRead });

        // Assert
        UserCodes(2).Should().BeEquivalentTo("PROC:SHIFTS", "SAGE:Base_1:READ", "SELF:READ");
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_UnknownCode_ThrowsAndChangesNothing()
    {
        // Arrange
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        var before = _uow.Snapshot();

        // Act
        Func<Task> act = () => _auth.SaveUserDetailedPermissionsAsync(1, 2, new[] { "PROC:SHIFTS", "INVALID:HACK:CODE" });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Codes de permission inconnus : INVALID:HACK:CODE.");
        _uow.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_CodeLongerThan128Chars_Throws()
    {
        // Arrange
        var longCode = "SAGE:" + new string('A', 119) + ":READ";

        // Act
        Func<Task> act = () => _auth.SaveUserDetailedPermissionsAsync(1, 2, new[] { longCode });

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().StartWith("Code de permission trop long (128 caractères maximum)");
        UserCodes(2).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_NullList_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () => _auth.SaveUserDetailedPermissionsAsync(1, 2, null!);

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message
            .Should().Be("La liste des permissions ne peut pas être nulle.");
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_UnchangedRoundTrip_KeepsSameRows()
    {
        // Arrange
        foreach (var code in new[] { "PROC:SHIFTS", "SAGE:BASE1:READ" })
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = code, IsGranted = true });
        var shown = await _auth.GetUserDetailedPermissionsAsync(2);

        // Act
        await _auth.SaveUserDetailedPermissionsAsync(1, 2, shown.GrantedPermissions);

        // Assert
        UserCodes(2).Should().BeEquivalentTo("PROC:SHIFTS", "SAGE:BASE1:READ");
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_LegacyDatabaseCodeWithoutAction_IsKeptWithItsGate()
    {
        // Arrange
        // « SAGE:ANCIENNE » (sans action) : masqué à l'écran, mais compté par le calculateur (porte Bases).
        foreach (var code in new[] { "PROC:SHIFTS", "SAGE:ANCIENNE" })
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = code, IsGranted = true });
        var shown = await _auth.GetUserDetailedPermissionsAsync(2);

        // Act
        await _auth.SaveUserDetailedPermissionsAsync(1, 2, shown.GrantedPermissions.Append("PARAMS:USERS").ToList());

        // Assert
        shown.GrantedPermissions.Should().NotContain("SAGE:ANCIENNE");
        UserCodes(2).Should().BeEquivalentTo("PROC:SHIFTS", "PARAMS:USERS", "SAGE:ANCIENNE");
        var rights = await SoftTime.Tests.Http.TestRights.Service(_uow).GetAsync(2, fresh: true);
        rights.HasGate(Rights.Databases).Should().BeTrue();
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_NewLegacyDatabaseCodeTyped_IsStillRefused()
    {
        // Act
        Func<Task> act = () => _auth.SaveUserDetailedPermissionsAsync(1, 2, new[] { "SAGE:ANCIENNE" });

        // Assert
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be("Codes de permission inconnus : SAGE:ANCIENNE.");
    }

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_LegacyDatabaseCodeWithoutAction_IsKeptWithItsGate()
    {
        // Arrange
        foreach (var code in new[] { "PROC:SHIFTS", "SAGE:ANCIENNE" })
            _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = code, IsGranted = true });
        var shown = await _auth.GetRoleDetailedPermissionsAsync(3);

        // Act
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, shown.GrantedPermissions);

        // Assert
        shown.GrantedPermissions.Should().Equal("PROC:SHIFTS");
        (await _uow.Get<RolePermission>().ListAsync(p => p.IdRole == 3)).Select(p => p.PermissionCode)
            .Should().BeEquivalentTo("PROC:SHIFTS", "SAGE:ANCIENNE");
        (await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 3)).Select(d => (int)d.IdDroit)
            .Should().BeEquivalentTo(new[] { Rights.Databases, Rights.Processing });
    }

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_LegacyUnknownCodeInDatabase_IsHiddenSoTheScreenCanSave()
    {
        // Arrange
        foreach (var code in new[] { "PROC:SHIFTS", "OBSOLETE:CODE" })
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = code, IsGranted = true });

        // Act
        var shown = await _auth.GetUserDetailedPermissionsAsync(2);
        await _auth.SaveUserDetailedPermissionsAsync(1, 2, shown.GrantedPermissions);

        // Assert
        shown.GrantedPermissions.Should().Equal("PROC:SHIFTS");
        UserCodes(2).Should().Equal("PROC:SHIFTS");
    }

    #endregion

    #region Migration 006 — Écarts au lieu de copies

    private async Task<IReadOnlySet<string>> EffectiveOf(decimal userId)
        => (await SoftTime.Tests.Http.TestRights.Service(_uow).GetAsync(userId, fresh: true)).Permissions;

    [Fact]
    public async Task SaveRoleDetailedPermissionsAsync_CustomizedAccount_FollowsTheRoleAndKeepsItsDeviations()
    {
        // Arrange
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PROC:SHIFTS:CREATE" });
        // Le compte ajoute PARAMS:USERS et retire PROC:SHIFTS:CREATE.
        await _auth.SaveUserDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PARAMS:USERS" });

        // Act
        // Le rôle gagne PROC:PUNCHES.
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:PUNCHES" });

        // Assert
        (await EffectiveOf(3)).Should().BeEquivalentTo("PROC:SHIFTS", "PROC:PUNCHES", "PARAMS:USERS");
        _uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == 3).Select(p => (p.PermissionCode, p.IsGranted, p.IsDeviation))
            .Should().BeEquivalentTo(new[] { ("PARAMS:USERS", true, true), ("PROC:SHIFTS:CREATE", false, true) });
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_EmptyList_StaysWithoutRightsWhenTheRoleGrows()
    {
        // Arrange
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS" });
        await _auth.SaveUserDetailedPermissionsAsync(1, 3, Array.Empty<string>());

        // Act
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PROC:PUNCHES" });

        // Assert
        (await EffectiveOf(3)).Should().BeEmpty("la liste vide est mémorisée par le marqueur « aucun droit »");
    }

    [Fact]
    public async Task SaveUserDetailedPermissionsAsync_ScreenRoundTrip_EffectiveRightsEqualTheRequestedList()
    {
        // Arrange
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PARAMS:HOLIDAYS" });
        var requested = new[] { "PROC:SHIFTS", "PARAMS:USERS", "OTHER:CANTEEN" };

        // Act
        await _auth.SaveUserDetailedPermissionsAsync(1, 3, requested);
        var shown = await _auth.GetUserDetailedPermissionsAsync(3);

        // Assert
        (await EffectiveOf(3)).Should().BeEquivalentTo(requested);
        shown.GrantedPermissions.Should().BeEquivalentTo(requested);
        shown.IsCustomized.Should().BeTrue();
    }

    #endregion

    #region B5, C1, C4 — Réinitialisation des permissions utilisateur

    [Fact]
    public async Task ResetUserDetailedPermissionsAsync_CustomizedAccount_ReturnsToRolePermissions()
    {
        // Arrange
        // Rôle 3 avec PROC:SHIFTS
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        // Utilisateur 3 personnalisé avec PARAMS:USERS
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true });
        // Reset par l'admin
        await _auth.ResetUserDetailedPermissionsAsync(currentUserId: 1, targetUserId: 3);

        // Act
        // T_USER_PERMISSION est vidé
        var userPerms = await _uow.Get<UserPermission>().ListAsync(p => p.IdResponsable == 3);

        // Assert
        userPerms.Should().BeEmpty();
        // L'utilisateur n'est plus personnalisé et hérite du rôle
        var perms = await _auth.GetUserDetailedPermissionsAsync(3);
        perms.IsCustomized.Should().BeFalse();
        perms.GrantedPermissions.Should().Contain("PROC:SHIFTS");
        // Journal d'audit créé
        var audit = await _uow.Get<AuditDroit>().ListAsync(a => a.TypeCible == "USER" && a.IdCible == 3);
        audit.Should().ContainSingle();
        audit[0].Action.Should().Be("RESET_USER_PERMS");
        audit[0].PermsAvant.Should().Be("PARAMS:USERS");
    }

    [Fact]
    public async Task ResetUserPermissions_TokenAdminDemotedInDatabase_Self_ThrowsForbidden()
    {
        // Act
        // Le service de test a un jeton administrateur ; le compte 2 n'est pas administrateur en base.
        Func<Task> act = () =>
            _auth.ResetUserDetailedPermissionsAsync(currentUserId: 2, targetUserId: 2);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<ForbiddenException>()).Which;
        ex.Message.Should().Be("Cette opération est réservée aux administrateurs.");
    }

    [Fact]
    public async Task UserPermissionWrites_TokenAdminDemotedInDatabase_OtherAccount_ThrowForbiddenAndChangeNothing()
    {
        // Arrange
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        var before = _uow.Snapshot();

        // Act
        Func<Task> save = () => _auth.SaveUserDetailedPermissionsAsync(currentUserId: 2, targetUserId: 3, new[] { "PARAMS:USERS" });
        Func<Task> reset = () => _auth.ResetUserDetailedPermissionsAsync(currentUserId: 2, targetUserId: 3);

        // Assert
        await save.Should().ThrowExactlyAsync<ForbiddenException>();
        await reset.Should().ThrowExactlyAsync<ForbiddenException>();
        _uow.Snapshot().Should().Be(before);
    }

    #endregion

    #region C5 — Nettoyage à la suppression d'un rôle

    [Fact]
    public async Task DeleteRoleAsync_RoleWithRows_CleansRolePermissionsAndDroitsRole()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 4, NomRole = "RoleToDelete" });
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 4, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 4, IdDroit = Rights.Processing });

        // Act
        await _auth.DeleteRoleAsync(4);

        // Assert
        (await _uow.Get<Role>().GetByIdAsync(4)).Should().BeNull();
        var remainingPerms = await _uow.Get<RolePermission>().ListAsync(p => p.IdRole == 4);
        remainingPerms.Should().BeEmpty();
        var remainingDroits = await _uow.Get<DroitRole>().ListAsync(d => d.IdRole == 4);
        remainingDroits.Should().BeEmpty();
    }

    #endregion

    #region C6 — Changement de rôle et resetPermissions

    [Fact]
    public async Task SaveUserAsync_ConfirmClearAdjustments_AppliedWhenRoleActuallyChanges()
    {
        // Arrange
        // Utilisateur 3 avec rôle 2 et personnalisation
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 2, Login = "user3", NomResponsable = "User 3" });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true });
        // Changement de rôle vers 3 avec ConfirmClearAdjustments = true
        var updateDto = new UserDto(3, 3, "User 3", "user3", null, null) { ConfirmClearAdjustments = true };
        await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Act
        // Permissions personnalisées purgées
        var userPerms = await _uow.Get<UserPermission>().ListAsync(p => p.IdResponsable == 3);

        // Assert
        userPerms.Should().BeEmpty();
        // Audit de réinitialisation créé
        var audit = await _uow.Get<AuditDroit>().ListAsync(a => a.TypeCible == "USER" && a.IdCible == 3 && a.Action == "RESET_USER_PERMS");
        audit.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveUserAsync_ConfirmClearAdjustments_IgnoredWhenRoleUnchanged()
    {
        // Arrange
        // Utilisateur 3 avec rôle 2 et personnalisation
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 2, Login = "user3", NomResponsable = "User 3" });
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 3, PermissionCode = "PARAMS:USERS", IsGranted = true });
        // Même rôle 2 avec ConfirmClearAdjustments = true
        var updateDto = new UserDto(3, 2, "User 3 Modif Nom", "user3", null, null) { ConfirmClearAdjustments = true };
        await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Act
        // Permissions personnalisées NON purgées (car le rôle n'a pas changé)
        var userPerms = await _uow.Get<UserPermission>().ListAsync(p => p.IdResponsable == 3);

        // Assert
        userPerms.Should().ContainSingle();
        // Aucun audit RESET_USER_PERMS
        var audit = await _uow.Get<AuditDroit>().ListAsync(a => a.TypeCible == "USER" && a.IdCible == 3 && a.Action == "RESET_USER_PERMS");
        audit.Should().BeEmpty();
    }

    #endregion

    #region In-Memory Test Doubles

    #endregion
}
