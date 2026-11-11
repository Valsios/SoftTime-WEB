using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Tests;

/// <summary>Étape 4b-1 : contrôle d'administrateur dans AuthService pour toute écriture sur un autre compte.</summary>
public class UserAdministrationGuardTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();

    public UserAdministrationGuardTests()
    {
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Gestionnaire" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 3, Login = "gestionnaire" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 2, Login = "cible" });
    }

    private AuthServices Service(decimal userId, decimal roleId)
        => new(_uow, new Token(), _mapper, new Mail(), new TestCurrentUser(userId, roleId), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());

    private static readonly UserDto NewUserDto = new(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");

    [Fact]
    public async Task SaveUserAsync_NonAdministrator_IsRefusedOnEveryWriteAndChangesNothing()
    {
        // Arrange
        var svc = Service(2, 3);

        // Act
        var before = _uow.Snapshot();

        // Assert
        await FluentActions.Awaiting(() => svc.SaveUserAsync(2, NewUserDto)).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserAsync(2, new UserDto(3, 3, "Cible", "cible", null, "ValidP@ss123!"))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.DeleteUserAsync(2, 3)).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SetDbAccessAsync(3, new[] { 1 })).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserDetailedPermissionsAsync(2, 3, new[] { "shifts.read" })).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.ResetUserDetailedPermissionsAsync(2, 3)).Should().ThrowExactlyAsync<ForbiddenException>();
        _uow.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task SaveUserAsync_Administrator_KeepsEveryExistingAbility()
    {
        // Arrange
        var sut = Service(1, 1);
        var created = await sut.SaveUserAsync(1, NewUserDto);

        // Act
        var changed = await sut.SaveUserAsync(1, new UserDto(created.Id, 3, "Nouveau", "nouveau", null, null));

        // Assert
        changed.RoleId.Should().Be(3);
        await sut.SaveUserDetailedPermissionsAsync(1, 3, new[] { "shifts.read" });
        await sut.ResetUserDetailedPermissionsAsync(1, 3);
        await sut.DeleteUserAsync(1, created.Id);
        _uow.Get<Responsable>().Items.Should().NotContain(u => u.IdResponsable == created.Id);
    }

    [Fact]
    public async Task SaveUserAsync_Administrator_ExistingProtectionsStillApply()
    {
        // Arrange
        var svc = Service(1, 1);

        // Act & Assert
        // Pas d'auto-modification du rôle, pas de suppression de son propre compte.
        await FluentActions.Awaiting(() => svc.SaveUserAsync(1, new UserDto(1, 2, "Admin", "admin", null, null))).Should().ThrowExactlyAsync<DomainException>();
        await FluentActions.Awaiting(() => svc.DeleteUserAsync(1, 1)).Should().ThrowExactlyAsync<DomainException>();
    }


    [Fact]
    public async Task SaveUserAsync_RoleNamedAdminButNotRole1_IsRefusedOnRole1AndRightsChanges()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 4, NomRole = "ROLE_ADMIN_CUSTOM" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 4, IdRole = 4, Login = "customadmin" });
        var svc = Service(4, 4);

        // Act
        var before = _uow.Snapshot();

        // Assert
        await FluentActions.Awaiting(() => svc.SaveUserAsync(4, new UserDto(0, 1, "Nouvel admin", "nouvel", null, "ValidP@ss123!"))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserAsync(4, new UserDto(3, 1, "Cible", "cible", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserDetailedPermissionsAsync(4, 3, new[] { "shifts.read" })).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.ResetUserDetailedPermissionsAsync(4, 3)).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SetDbAccessAsync(3, new[] { 1 })).Should().ThrowExactlyAsync<ForbiddenException>();
        // Même un rôle nommé comme l'administrateur ne peut pas modifier son propre compte de droits.
        await FluentActions.Awaiting(() => svc.SaveUserDetailedPermissionsAsync(4, 4, new[] { "users.read" })).Should().ThrowExactlyAsync<ForbiddenException>();
        _uow.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task SaveUserAsync_Role4_IsRefusedOnEveryGuardedOverload()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 4, NomRole = "ROLE_ADMIN_CUSTOM" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 4, IdRole = 4, Login = "customadmin" });
        var svc = Service(4, 4);

        // Act
        var before = _uow.Snapshot();

        // Assert
        // Créer, modifier, attribuer le rôle 1, rétrograder le compte 1 : la garde refuse avant tout contrôle interne.
        await FluentActions.Awaiting(() => svc.SaveUserAsync(4, new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!"))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserAsync(4, new UserDto(3, 2, "Cible modifiée", "cible", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserAsync(4, new UserDto(3, 1, "Cible", "cible", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserAsync(4, new UserDto(1, 2, "Admin", "admin", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        // Supprimer, accès aux bases, droits, réinitialisation.
        await FluentActions.Awaiting(() => svc.DeleteUserAsync(4, 3)).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.DeleteUserAsync(4, 1)).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SetDbAccessAsync(3, new[] { 1 })).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.SaveUserDetailedPermissionsAsync(4, 3, new[] { "shifts.read" })).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.ResetUserDetailedPermissionsAsync(4, 3)).Should().ThrowExactlyAsync<ForbiddenException>();
        _uow.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task SaveUserAsync_Role4ChangingOwnRole_IsRefused()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 4, NomRole = "ROLE_ADMIN_CUSTOM" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 4, IdRole = 4, Login = "customadmin" });

        // Act
        var before = _uow.Snapshot();

        // Assert
        await FluentActions.Awaiting(() =>
            Service(4, 4).SaveUserAsync(4, new UserDto(4, 1, "customadmin", "customadmin", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        await FluentActions.Awaiting(() =>
            Service(4, 4).SaveUserAsync(4, new UserDto(4, 2, "customadmin", "customadmin", null, null))).Should().ThrowExactlyAsync<ForbiddenException>();
        _uow.Snapshot().Should().Be(before);
        _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 4).IdRole.Should().Be(4);
    }

    [Fact]
    public async Task DeleteUserAsync_Role1_CannotDeleteItselfOrTheLastAdministrator()
    {
        // Arrange
        var svc = Service(1, 1);

        // Act & Assert
        var own = (await FluentActions.Awaiting(() => svc.DeleteUserAsync(1, 1)).Should().ThrowExactlyAsync<DomainException>()).Which;
        own.Message.Should().Be("Vous ne pouvez pas supprimer votre propre compte.");
        // Dernier administrateur : suppression et rétrogradation refusées (surcharges sans appelant, utilisées par les tests historiques).
        var delete = (await FluentActions.Awaiting(() => svc.DeleteUserAsync(1)).Should().ThrowExactlyAsync<ConflictException>()).Which;
        delete.Message.Should().Be("Impossible de supprimer le dernier administrateur.");
        var demote = (await FluentActions.Awaiting(() => svc.SaveUserAsync(new UserDto(1, 2, "Admin", "admin", null, null))).Should().ThrowExactlyAsync<ConflictException>()).Which;
        demote.Message.Should().Be("Impossible de rétrograder le dernier administrateur.");
        _uow.Get<Responsable>().Items.Should().Contain(u => u.IdResponsable == 1 && u.IdRole == 1);
    }

    [Fact]
    public async Task DeleteUserAsync_Role1_MustKeepOneAdministrator()
    {
        // Arrange
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 5, IdRole = 1, Login = "admin2" });
        var sut = Service(1, 1);
        await sut.SaveUserAsync(1, new UserDto(5, 1, "Admin 2 renommé", "admin2", null, null));

        // Act
        await sut.DeleteUserAsync(1, 5);

        // Assert
        _uow.Get<Responsable>().Items.Should().NotContain(u => u.IdResponsable == 5);
    }

    private sealed class Token : ITokenService
    {
        public string CreateToken(TokenSubject subject) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
