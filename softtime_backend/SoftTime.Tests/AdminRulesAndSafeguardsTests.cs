using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class AdminRulesAndSafeguardsTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;
    private readonly IEmailService _email;
    private readonly PasswordHasher<Responsable> _hasher;
    private readonly AuthServices _auth;

    public AdminRulesAndSafeguardsTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = ServiceMocks.TokenService().Object;

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = ServiceMocks.EmailService().Object;
        _hasher = new PasswordHasher<Responsable>();
        _auth = new AuthServices(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

        // Configuration initiale des rôles de référence
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "ROLE_ADMIN_CUSTOM" });
    }

    /// <summary>Service dont l'appelant HTTP (ICurrentUser) est le compte indiqué ; _auth, lui, est toujours un administrateur.</summary>
    private AuthServices CallerAuth(decimal userId, decimal roleId)
        => new(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.TestCurrentUser(userId, roleId), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

    #region Tests AdminRules (G5, C3)

    [Fact]
    public void AdminRules_IsAdmin_IdentifiesAdminRoleId()
    {
        // Act & Assert
        AdminRules.IsAdmin(1, null).Should().BeTrue();
        AdminRules.IsAdmin(1, "AutreNom").Should().BeTrue();
    }

    [Fact]
    public void AdminRules_IsAdmin_DoesNotRecognizeRoleNameContainingAdmin()
    {
        // Act & Assert
        // Règle 4b-2 : seul le rôle 1 est administrateur, le nom du rôle ne confère rien (avant : ces quatre cas étaient vrais).
        AdminRules.IsAdmin(2, "Administrateur").Should().BeFalse();
        AdminRules.IsAdmin(3, "ROLE_ADMIN_CUSTOM").Should().BeFalse();
        AdminRules.IsAdmin(4, "super-admin").Should().BeFalse();
        AdminRules.IsAdmin(5, "Administrateur RH").Should().BeFalse();
        AdminRules.IsAdmin(new Role { IdRole = 3, NomRole = "ROLE_ADMIN_CUSTOM" }).Should().BeFalse();
        AdminRules.IsAdmin(new Role { IdRole = 1, NomRole = "Autre nom" }).Should().BeTrue();
    }

    [Fact]
    public void AdminRules_IsAdmin_RejectsNonAdminRole()
    {
        // Act & Assert
        AdminRules.IsAdmin(2, "Utilisateur").Should().BeFalse();
        AdminRules.IsAdmin(2, "Opérateur").Should().BeFalse();
        AdminRules.IsAdmin(2, null).Should().BeFalse();
        AdminRules.IsAdmin(2, "").Should().BeFalse();
    }

    [Fact]
    public void AdminRules_IsAdmin_WithNullRoleEntity_ReturnsFalse()
    {
        // Arrange
        Role? nullRole = null;

        // Act
        var result = AdminRules.IsAdmin(nullRole);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void AdminRules_IsSuperAdmin_ReturnsTrueOnlyForAdminRoleId()
    {
        // Act & Assert
        AdminRules.IsSuperAdmin(1).Should().BeTrue();
        AdminRules.IsSuperAdmin(2).Should().BeFalse();
        AdminRules.IsSuperAdmin(3).Should().BeFalse();
    }

    #endregion

    #region Tests C2 — Appelant Inconnu

    [Fact]
    public async Task SaveUserAsync_UnknownCaller_ThrowsDomainException()
    {
        // Arrange
        var dto = new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(currentUserId: 999, dto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Utilisateur appelant introuvable.");
    }

    [Fact]
    public async Task SaveUserAsync_CallerWithMissingRole_ThrowsDomainException()
    {
        // Arrange
        var caller = new Responsable { IdResponsable = 10, IdRole = 999, Login = "callerOrphan", NomResponsable = "Orphan" };
        _uow.Get<Responsable>().Items.Add(caller);
        var dto = new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(currentUserId: 10, dto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Utilisateur appelant introuvable.");
    }

    [Fact]
    public async Task DeleteUserAsync_UnknownCaller_ThrowsDomainException()
    {
        // Act
        Func<Task> act = () => _auth.DeleteUserAsync(currentUserId: 999, id: 2);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Utilisateur appelant introuvable.");
    }

    #endregion

    #region Tests G1 — Auto-modification de rôle

    [Fact]
    public async Task SaveUserAsync_SelfRoleModification_ThrowsDomainException()
    {
        // Arrange
        var admin = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin" };
        var admin2 = new Responsable { IdResponsable = 2, IdRole = 1, Login = "admin2", NomResponsable = "Admin 2" };
        _uow.Get<Responsable>().Items.Add(admin);
        _uow.Get<Responsable>().Items.Add(admin2);
        // L'admin 1 tente de changer son propre rôle vers le rôle 2
        var updateDto = new UserDto(1, 2, "Admin Modifié", "admin", null, null);

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Vous ne pouvez pas modifier votre propre rôle.");
    }

    [Fact]
    public async Task SaveUserAsync_SelfUpdateWithoutRoleChange_Succeeds()
    {
        // Arrange
        var admin = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin Initial", Email = "old@softtime.local" };
        _uow.Get<Responsable>().Items.Add(admin);
        var updateDto = new UserDto(1, 1, "Admin Nouveau Nom", "admin", "MAT-01", null) { Email = "new@softtime.local" };

        // Act
        var result = await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        // Assert
        result.Nom.Should().Be("Admin Nouveau Nom");
        result.Email.Should().Be("new@softtime.local");
        result.RoleId.Should().Be(1);
    }

    #endregion

    #region Tests G2 — Attribution rôle admin et modification compte admin

    [Fact]
    public async Task SaveUserAsync_NonAdmin_AssigningAdminRole_ThrowsDomainException()
    {
        // Arrange
        var user = new Responsable { IdResponsable = 2, IdRole = 2, Login = "user", NomResponsable = "User" };
        _uow.Get<Responsable>().Items.Add(user);
        // Un utilisateur non-admin tente de créer un super-admin (rôle 1)
        var createDto = new UserDto(0, 1, "Nouvel Admin", "newadmin", null, "ValidP@ss123!");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(currentUserId: 2, createDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Seul un super-administrateur peut attribuer le rôle Administrateur.");
    }

    [Fact]
    public async Task SaveUserAsync_NonAdmin_ModifyingAdminAccount_ThrowsDomainException()
    {
        // Arrange
        var admin = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin" };
        var user = new Responsable { IdResponsable = 2, IdRole = 2, Login = "user", NomResponsable = "User" };
        _uow.Get<Responsable>().Items.Add(admin);
        _uow.Get<Responsable>().Items.Add(user);
        // L'utilisateur tente de modifier le mot de passe du super-admin (rôle 1)
        var updateDto = new UserDto(1, 1, "Admin Hacké", "admin", null, "NewPass123!@");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(currentUserId: 2, updateDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Seul un super-administrateur peut modifier un compte administrateur.");
    }

    [Fact]
    public async Task DeleteUserAsync_NonAdmin_DeletingAdminAccount_ThrowsDomainException()
    {
        // Arrange
        var admin = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin" };
        var user = new Responsable { IdResponsable = 2, IdRole = 2, Login = "user", NomResponsable = "User" };
        _uow.Get<Responsable>().Items.Add(admin);
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        Func<Task> act = () => _auth.DeleteUserAsync(currentUserId: 2, id: 1);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Seul un super-administrateur peut supprimer un compte administrateur.");
    }

    [Fact]
    public async Task NonAdmin_CustomAdminRoleOperations_AreRefusedByTheAdministratorGuard()
    {
        // Arrange
        // Règle 4b-2 : le rôle 3 « ROLE_ADMIN_CUSTOM » n'est plus un rôle administrateur. Toute opération sur des comptes
        // par un appelant du rôle 2 est refusée dès l'entrée (avant : InvalidOperationException « Seul un administrateur… »).
        var user = new Responsable { IdResponsable = 2, IdRole = 2, Login = "user", NomResponsable = "User" };
        var customAdmin = new Responsable { IdResponsable = 3, IdRole = 3, Login = "customadmin", NomResponsable = "Custom Admin" };
        _uow.Get<Responsable>().Items.Add(user);
        _uow.Get<Responsable>().Items.Add(customAdmin);
        var asUser = CallerAuth(2, 2);
        var createDto = new UserDto(0, 3, "New Custom Admin", "newcustom", null, "ValidP@ss123!");

        // Act & Assert
        var exCreate = (await FluentActions.Awaiting(() => asUser.SaveUserAsync(currentUserId: 2, createDto)).Should().ThrowExactlyAsync<ForbiddenException>()).Which;
        exCreate.Message.Should().Be(AdminAccess.ReservedMessage);
        var updateDto = new UserDto(3, 3, "Custom Admin Modifié", "customadmin", null, null);
        var exMod = (await FluentActions.Awaiting(() => asUser.SaveUserAsync(currentUserId: 2, updateDto)).Should().ThrowExactlyAsync<ForbiddenException>()).Which;
        exMod.Message.Should().Be(AdminAccess.ReservedMessage);
        var exDel = (await FluentActions.Awaiting(() => asUser.DeleteUserAsync(currentUserId: 2, id: 3)).Should().ThrowExactlyAsync<ForbiddenException>()).Which;
        exDel.Message.Should().Be(AdminAccess.ReservedMessage);
        _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 3).NomResponsable.Should().Be("Custom Admin");
    }

    [Fact]
    public async Task SaveUserAsync_CustomAdmin_CannotAssignRole1_CreationAndModification()
    {
        // Arrange
        var customAdmin = new Responsable { IdResponsable = 3, IdRole = 3, Login = "customadmin", NomResponsable = "Custom Admin" };
        var ordinaryUser = new Responsable { IdResponsable = 4, IdRole = 2, Login = "user4", NomResponsable = "User 4" };
        _uow.Get<Responsable>().Items.Add(customAdmin);
        _uow.Get<Responsable>().Items.Add(ordinaryUser);
        // Création avec rôle 1 par un admin personnalisé
        var createDto = new UserDto(0, 1, "New SuperAdmin", "newsuper", null, "ValidP@ss123!");

        // Act & Assert
        var exCreate = (await FluentActions.Awaiting(() => _auth.SaveUserAsync(currentUserId: 3, createDto)).Should().ThrowExactlyAsync<DomainException>()).Which;
        exCreate.Message.Should().Be("Seul un super-administrateur peut attribuer le rôle Administrateur.");
        // Modification avec rôle 1 par un admin personnalisé
        var updateDto = new UserDto(4, 1, "User 4 Promu", "user4", null, null);
        var exUpdate = (await FluentActions.Awaiting(() => _auth.SaveUserAsync(currentUserId: 3, updateDto)).Should().ThrowExactlyAsync<DomainException>()).Which;
        exUpdate.Message.Should().Be("Seul un super-administrateur peut attribuer le rôle Administrateur.");
    }

    [Fact]
    public async Task CustomAdmin_CannotModifyOrDeleteRole1Account_EvenIfAnotherSuperAdminExists()
    {
        // Arrange
        var admin1 = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin1", NomResponsable = "Admin 1" };
        var admin2 = new Responsable { IdResponsable = 2, IdRole = 1, Login = "admin2", NomResponsable = "Admin 2" };
        var customAdmin = new Responsable { IdResponsable = 3, IdRole = 3, Login = "customadmin", NomResponsable = "Custom Admin" };
        _uow.Get<Responsable>().Items.Add(admin1);
        _uow.Get<Responsable>().Items.Add(admin2);
        _uow.Get<Responsable>().Items.Add(customAdmin);
        // Tentative de modification du compte rôle 1
        var updateDto = new UserDto(1, 1, "Admin 1 Renommé", "admin1", null, null);

        // Act & Assert
        var exMod = (await FluentActions.Awaiting(() => _auth.SaveUserAsync(currentUserId: 3, updateDto)).Should().ThrowExactlyAsync<DomainException>()).Which;
        exMod.Message.Should().Be("Seul un super-administrateur peut modifier un compte administrateur.");
        // Tentative de suppression du compte rôle 1
        var exDel = (await FluentActions.Awaiting(() => _auth.DeleteUserAsync(currentUserId: 3, id: 1)).Should().ThrowExactlyAsync<DomainException>()).Which;
        exDel.Message.Should().Be("Seul un super-administrateur peut supprimer un compte administrateur.");
    }

    [Fact]
    public async Task CustomAdmin_CannotCreateModifyOrDeleteOrdinaryAccount_AfterRule4b2()
    {
        // Arrange
        // Règle 4b-2 (avant : un rôle dont le nom contient ADMIN pouvait créer, modifier et supprimer un compte ordinaire).
        var customAdmin = new Responsable { IdResponsable = 3, IdRole = 3, Login = "customadmin", NomResponsable = "Custom Admin" };
        var ordinary = new Responsable { IdResponsable = 5, IdRole = 2, Login = "employe1", NomResponsable = "Employé 1" };
        _uow.Get<Responsable>().Items.Add(customAdmin);
        _uow.Get<Responsable>().Items.Add(ordinary);
        var asCustom = CallerAuth(3, 3);

        // Act & Assert
        await FluentActions.Awaiting(() => asCustom.SaveUserAsync(currentUserId: 3, new UserDto(0, 2, "Employé 2", "employe2", null, "ValidP@ss123!"))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => asCustom.SaveUserAsync(currentUserId: 3, new UserDto(5, 2, "Employé 1 Modifié", "employe1_mod", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => asCustom.DeleteUserAsync(currentUserId: 3, id: 5)).Should().ThrowExactlyAsync<ForbiddenException>();
        _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 5).Login.Should().Be("employe1");
    }

    [Fact]
    public async Task OrdinaryUser_CannotCreateModifyOrDeleteAccounts_AfterStep4b1()
    {
        // Arrange
        // Étape 4b-1 (avant : un compte ordinaire pouvait gérer des comptes ordinaires au niveau du service).
        var ordinaryCaller = new Responsable { IdResponsable = 2, IdRole = 2, Login = "user2", NomResponsable = "User 2" };
        var other = new Responsable { IdResponsable = 6, IdRole = 2, Login = "employe3", NomResponsable = "Employé 3" };
        _uow.Get<Responsable>().Items.Add(ordinaryCaller);
        _uow.Get<Responsable>().Items.Add(other);
        var asOrdinary = CallerAuth(2, 2);

        // Act & Assert
        await FluentActions.Awaiting(() => asOrdinary.SaveUserAsync(currentUserId: 2, new UserDto(0, 2, "Employé 2", "employe2", null, "ValidP@ss123!"))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => asOrdinary.SaveUserAsync(currentUserId: 2, new UserDto(6, 2, "Employé 3 Modifié", "employe3_mod", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => asOrdinary.DeleteUserAsync(currentUserId: 2, id: 6)).Should().ThrowExactlyAsync<ForbiddenException>();
        _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 6).Login.Should().Be("employe3");
    }

    [Fact]
    public async Task SaveUserAsync_SuperAdminAssigningRole1_Succeeds()
    {
        // Arrange
        var superAdmin = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin" };
        _uow.Get<Responsable>().Items.Add(superAdmin);
        // Création d'un nouvel administrateur rôle 1
        var createDto = new UserDto(0, 1, "Admin 2", "admin2", null, "ValidP@ss123!");

        // Act
        var created = await _auth.SaveUserAsync(currentUserId: 1, createDto);

        // Assert
        created.RoleId.Should().Be(1);
        created.Login.Should().Be("admin2");
        // Promotion d'un compte ordinaire vers le rôle 1
        var user = new Responsable { IdResponsable = 10, IdRole = 2, Login = "user10", NomResponsable = "User 10" };
        _uow.Get<Responsable>().Items.Add(user);
        var updateDto = new UserDto(10, 1, "User 10 Promu", "user10", null, null);
        var promoted = await _auth.SaveUserAsync(currentUserId: 1, updateDto);
        promoted.RoleId.Should().Be(1);
    }

    #endregion

    #region Tests G3 — Dernier Administrateur

    [Fact]
    public async Task SaveUserAsync_DemotingOnlyAdmin_ThrowsConflictException()
    {
        // Arrange
        var admin1 = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin1", NomResponsable = "Admin 1" };
        var admin2 = new Responsable { IdResponsable = 2, IdRole = 1, Login = "admin2", NomResponsable = "Admin 2" };
        _uow.Get<Responsable>().Items.Add(admin1);
        _uow.Get<Responsable>().Items.Add(admin2);
        // Admin 2 rétrograde Admin 1 vers le rôle 2 -> Permis car Admin 2 reste administrateur
        var updateDto = new UserDto(1, 2, "Admin 1", "admin1", null, null);

        // Act
        var res = await _auth.SaveUserAsync(currentUserId: 2, updateDto);

        // Assert
        res.RoleId.Should().Be(2);
        // Maintenant Admin 1 est rôle 2. Si un admin tente de rétrograder le seul admin restant (Admin 2):
        // Note: Admin 2 ne peut pas rétrograder son propre rôle (G1), mais créons un test où l'appelant tente de rétrograder le dernier admin restant
        // Simulons un troisième compte avec un rôle de délégation ou appel direct
        var updateLastAdmin = new UserDto(2, 2, "Admin 2", "admin2", null, null);
        var ex = (await FluentActions.Awaiting(() => _auth.SaveUserAsync(updateLastAdmin)).Should().ThrowExactlyAsync<ConflictException>()).Which;
        ex.Message.Should().Be("Impossible de rétrograder le dernier administrateur.");
    }

    [Fact]
    public async Task SaveUserAsync_G3_IgnoresCustomRoleContainingAdminWhenCountingSuperAdmins()
    {
        // Arrange
        // Un seul vrai admin (IDROLE = 1) et un compte avec un rôle custom dont le nom contient "ADMIN" (IDROLE = 3)
        var superAdmin = new Responsable { IdResponsable = 1, IdRole = 1, Login = "superadmin", NomResponsable = "Super Admin" };
        var customAdmin = new Responsable { IdResponsable = 2, IdRole = 3, Login = "customadmin", NomResponsable = "Custom Admin" };
        _uow.Get<Responsable>().Items.Add(superAdmin);
        _uow.Get<Responsable>().Items.Add(customAdmin);
        // Tenter de rétrograder le seul superAdmin (IDROLE = 1) vers rôle 2 via la surcharge sans appelant
        var updateDto = new UserDto(1, 2, "Super Admin", "superadmin", null, null);

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(updateDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<ConflictException>()).Which;
        ex.Message.Should().Be("Impossible de rétrograder le dernier administrateur.");
    }

    [Fact]
    public async Task DeleteUserAsync_DeletingOnlyAdmin_ThrowsConflictException()
    {
        // Arrange
        var admin = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin" };
        _uow.Get<Responsable>().Items.Add(admin);

        // Act
        Func<Task> act = () => _auth.DeleteUserAsync(1);

        // Assert
        // Surcharge de test sans appelant
        var ex = (await act.Should().ThrowExactlyAsync<ConflictException>()).Which;
        ex.Message.Should().Be("Impossible de supprimer le dernier administrateur.");
    }

    [Fact]
    public async Task DeleteUserAsync_DeletingAdminWhenAnotherExists_Succeeds()
    {
        // Arrange
        var admin1 = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin1", NomResponsable = "Admin 1" };
        var admin2 = new Responsable { IdResponsable = 2, IdRole = 1, Login = "admin2", NomResponsable = "Admin 2" };
        _uow.Get<Responsable>().Items.Add(admin1);
        _uow.Get<Responsable>().Items.Add(admin2);

        // Act
        // Admin 1 supprime Admin 2
        await _auth.DeleteUserAsync(currentUserId: 1, id: 2);

        // Assert
        (await _uow.Get<Responsable>().GetByIdAsync(2)).Should().BeNull();
        (await _uow.Get<Responsable>().GetByIdAsync(1)).Should().NotBeNull();
    }

    #endregion

    #region Tests G4 — Auto-suppression

    [Fact]
    public async Task DeleteUserAsync_SelfDelete_ThrowsDomainException()
    {
        // Arrange
        var admin1 = new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin1", NomResponsable = "Admin 1" };
        var admin2 = new Responsable { IdResponsable = 2, IdRole = 1, Login = "admin2", NomResponsable = "Admin 2" };
        _uow.Get<Responsable>().Items.Add(admin1);
        _uow.Get<Responsable>().Items.Add(admin2);

        // Act
        Func<Task> act = () => _auth.DeleteUserAsync(currentUserId: 1, id: 1);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be("Vous ne pouvez pas supprimer votre propre compte.");
    }

    #endregion

    #region In-Memory Test Doubles

    #endregion
}
