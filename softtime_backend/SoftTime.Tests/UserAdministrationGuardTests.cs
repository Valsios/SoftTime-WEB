using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Étape 4b-1 : contrôle d'administrateur dans AuthService pour toute écriture sur un autre compte.</summary>
public class UserAdministrationGuardTests
{
    private readonly SnapshotInMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();

    public UserAdministrationGuardTests()
    {
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Gestionnaire" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 3, Login = "gestionnaire" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 2, Login = "cible" });
    }

    private AuthServices Service(decimal userId, decimal roleId)
        => new(_uow, new Token(), _mapper, new Mail(), new FakeUser(userId, roleId), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());

    private static readonly UserDto NewUserDto = new(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");

    [Fact]
    public async Task Non_administrator_is_refused_on_every_write_and_nothing_changes()
    {
        var svc = Service(2, 3);
        var before = _uow.Snapshot();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(2, NewUserDto));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(2, new UserDto(3, 3, "Cible", "cible", null, "ValidP@ss123!")));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteUserAsync(2, 3));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SetDbAccessAsync(3, new[] { 1 }));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserDetailedPermissionsAsync(2, 3, new[] { "PROC:SHIFTS" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ResetUserDetailedPermissionsAsync(2, 3));

        Assert.Equal(before, _uow.Snapshot());
    }

    [Fact]
    public async Task Administrator_keeps_every_existing_ability()
    {
        var svc = Service(1, 1);
        var created = await svc.SaveUserAsync(1, NewUserDto);
        var changed = await svc.SaveUserAsync(1, new UserDto(created.Id, 3, "Nouveau", "nouveau", null, null));
        Assert.Equal(3, changed.RoleId);
        await svc.SaveUserDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS" });
        await svc.ResetUserDetailedPermissionsAsync(1, 3);
        await svc.DeleteUserAsync(1, created.Id);
        Assert.DoesNotContain(_uow.GetRepo<Responsable>().Items, u => u.IdResponsable == created.Id);
    }

    [Fact]
    public async Task Existing_protections_still_apply_to_an_administrator()
    {
        var svc = Service(1, 1);
        // Pas d'auto-modification du rôle, pas de suppression de son propre compte.
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveUserAsync(1, new UserDto(1, 2, "Admin", "admin", null, null)));
        await Assert.ThrowsAsync<DomainException>(() => svc.DeleteUserAsync(1, 1));
    }


    [Fact]
    public async Task Role_named_admin_but_not_role_1_is_refused_by_the_guard_on_role_1_assignment_and_on_rights_changes()
    {
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 4, NomRole = "ROLE_ADMIN_CUSTOM" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 4, IdRole = 4, Login = "customadmin" });
        var svc = Service(4, 4);
        var before = _uow.Snapshot();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(4, new UserDto(0, 1, "Nouvel admin", "nouvel", null, "ValidP@ss123!")));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(4, new UserDto(3, 1, "Cible", "cible", null, null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserDetailedPermissionsAsync(4, 3, new[] { "PROC:SHIFTS" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ResetUserDetailedPermissionsAsync(4, 3));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SetDbAccessAsync(3, new[] { 1 }));
        // Même un rôle nommé comme l'administrateur ne peut pas modifier son propre compte de droits.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserDetailedPermissionsAsync(4, 4, new[] { "PARAMS:USERS" }));
        Assert.Equal(before, _uow.Snapshot());
    }

    [Fact]
    public async Task Role_4_is_refused_on_every_guarded_overload_and_state_is_unchanged()
    {
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 4, NomRole = "ROLE_ADMIN_CUSTOM" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 4, IdRole = 4, Login = "customadmin" });
        var svc = Service(4, 4);
        var before = _uow.Snapshot();

        // Créer, modifier, attribuer le rôle 1, rétrograder le compte 1 : la garde refuse avant tout contrôle interne.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(4, new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!")));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(4, new UserDto(3, 2, "Cible modifiée", "cible", null, null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(4, new UserDto(3, 1, "Cible", "cible", null, null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserAsync(4, new UserDto(1, 2, "Admin", "admin", null, null)));
        // Supprimer, accès aux bases, droits, réinitialisation.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteUserAsync(4, 3));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteUserAsync(4, 1));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SetDbAccessAsync(3, new[] { 1 }));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveUserDetailedPermissionsAsync(4, 3, new[] { "PROC:SHIFTS" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ResetUserDetailedPermissionsAsync(4, 3));

        Assert.Equal(before, _uow.Snapshot());
    }

    [Fact]
    public async Task Role_4_changing_its_own_role_is_refused_the_same_way()
    {
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 4, NomRole = "ROLE_ADMIN_CUSTOM" });
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 4, IdRole = 4, Login = "customadmin" });
        var before = _uow.Snapshot();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(4, 4).SaveUserAsync(4, new UserDto(4, 1, "customadmin", "customadmin", null, null)));   // vers le rôle 1
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(4, 4).SaveUserAsync(4, new UserDto(4, 2, "customadmin", "customadmin", null, null)));   // vers un autre rôle

        Assert.Equal(before, _uow.Snapshot());
        Assert.Equal(4, _uow.GetRepo<Responsable>().Items.Single(u => u.IdResponsable == 4).IdRole);
    }

    [Fact]
    public async Task Role_1_stays_protected_against_deleting_itself_and_removing_the_last_administrator()
    {
        var svc = Service(1, 1);
        var own = await Assert.ThrowsAsync<DomainException>(() => svc.DeleteUserAsync(1, 1));
        Assert.Equal("Vous ne pouvez pas supprimer votre propre compte.", own.Message);

        // Dernier administrateur : suppression et rétrogradation refusées (surcharges sans appelant, utilisées par les tests historiques).
        var delete = await Assert.ThrowsAsync<DomainException>(() => svc.DeleteUserAsync(1));
        Assert.Equal("Impossible de supprimer le dernier administrateur.", delete.Message);
        var demote = await Assert.ThrowsAsync<DomainException>(() => svc.SaveUserAsync(new UserDto(1, 2, "Admin", "admin", null, null)));
        Assert.Equal("Impossible de rétrograder le dernier administrateur.", demote.Message);
        Assert.Contains(_uow.GetRepo<Responsable>().Items, u => u.IdResponsable == 1 && u.IdRole == 1);
    }

    [Fact]
    public async Task Role_1_cannot_modify_or_delete_another_administrator_without_keeping_one()
    {
        _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 5, IdRole = 1, Login = "admin2" });
        var svc = Service(1, 1);
        await svc.SaveUserAsync(1, new UserDto(5, 1, "Admin 2 renommé", "admin2", null, null));
        await svc.DeleteUserAsync(1, 5);
        Assert.DoesNotContain(_uow.GetRepo<Responsable>().Items, u => u.IdResponsable == 5);
    }

    private sealed class FakeUser : ICurrentUser
    {
        public FakeUser(decimal userId, decimal roleId) { UserId = userId; RoleId = roleId; }
        public decimal? UserId { get; }
        public string? Login => $"u{UserId}";
        public decimal? RoleId { get; }
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => RoleId == 1;
    }

    private sealed class Token : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
