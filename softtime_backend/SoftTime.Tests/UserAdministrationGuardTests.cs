using AutoMapper;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Étape 4b-1 : contrôle d'administrateur dans AuthService pour toute écriture sur un autre compte.</summary>
public class UserAdministrationGuardTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();

    public UserAdministrationGuardTests()
    {
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Utilisateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 3, NOMROLE = "Gestionnaire" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 3, LOGIN = "gestionnaire" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 2, LOGIN = "cible" });
    }

    private AuthService Service(decimal userId, decimal roleId)
        => new(_uow, new Token(), _mapper, new Mail(), new PasswordHasher<T_RESPONSABLE>(), new FakeUser(userId, roleId));

    private static readonly UserDto NewUserDto = new(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");

    [Fact]
    public async Task Non_administrator_is_refused_on_every_write_and_nothing_changes()
    {
        var svc = Service(2, 3);
        var before = _uow.Snapshot();

        await Assert.ThrowsAsync<ForbiddenOperationException>(() => svc.SaveUserAsync(2, NewUserDto));
        await Assert.ThrowsAsync<ForbiddenOperationException>(() => svc.SaveUserAsync(2, new UserDto(3, 3, "Cible", "cible", null, "ValidP@ss123!")));
        await Assert.ThrowsAsync<ForbiddenOperationException>(() => svc.DeleteUserAsync(2, 3));
        await Assert.ThrowsAsync<ForbiddenOperationException>(() => svc.SetDbAccessAsync(3, new[] { 1 }));
        await Assert.ThrowsAsync<ForbiddenOperationException>(() => svc.SaveUserDetailedPermissionsAsync(2, 3, new[] { "PROC:SHIFTS" }));
        await Assert.ThrowsAsync<ForbiddenOperationException>(() => svc.ResetUserDetailedPermissionsAsync(2, 3));

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
        Assert.DoesNotContain(_uow.GetRepo<T_RESPONSABLE>().Items, u => u.IDRESPONSABLE == created.Id);
    }

    [Fact]
    public async Task Existing_protections_still_apply_to_an_administrator()
    {
        var svc = Service(1, 1);
        // Pas d'auto-modification du rôle, pas de suppression de son propre compte.
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveUserAsync(1, new UserDto(1, 2, "Admin", "admin", null, null)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteUserAsync(1, 1));
    }

    [Fact]
    public async Task Without_a_current_user_the_service_behaves_as_before()
    {
        // Les tests unitaires historiques construisent le service sans ICurrentUser : comportement inchangé.
        var legacy = new AuthService(_uow, new Token(), _mapper, new Mail(), new PasswordHasher<T_RESPONSABLE>());
        var created = await legacy.SaveUserAsync(2, NewUserDto);
        Assert.Equal("nouveau", created.Login);
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
