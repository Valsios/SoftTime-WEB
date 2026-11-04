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

/// <summary>Premier démarrage : create-admin exige le rôle d'identifiant 1, seul rôle administrateur.</summary>
public class CreateAdminFirstRunTests
{
    private const string Missing = "Le rôle Administrateur (identifiant 1) est introuvable : exécutez le script de création de la base.";

    private readonly SnapshotInMemoryUnitOfWork _uow = new();
    private readonly AuthService _auth;

    public CreateAdminFirstRunTests()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthService(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<T_RESPONSABLE>());
    }

    private static AdminSetupDto Dto => new("Admin Test", "admin", "ValidP@ss123!", null);

    [Fact]
    public async Task Fresh_database_with_role_1_creates_an_account_with_IDROLE_1()
    {
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Utilisateur" });
        _uow.GetRepo<T_DROIT>().Items.Add(new T_DROIT { IDDROIT = 1, DROIT = "Bases" });

        await _auth.CreateAdminAsync(Dto);

        var created = Assert.Single(_uow.GetRepo<T_RESPONSABLE>().Items);
        Assert.Equal(1, created.IDROLE);
        Assert.Contains(_uow.GetRepo<T_DROIT_ROLE>().Items, d => d.IDROLE == 1 && d.IDDROIT == 1);
    }

    [Fact]
    public async Task Administrator_role_with_another_identifier_is_refused_and_no_account_is_created()
    {
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Administrateur" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.CreateAdminAsync(Dto));

        Assert.Equal(Missing, ex.Message);
        Assert.Empty(_uow.GetRepo<T_RESPONSABLE>().Items);
        Assert.Empty(_uow.GetRepo<T_DROIT_ROLE>().Items);
    }

    [Fact]
    public async Task Existing_account_is_still_refused()
    {
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 5, IDROLE = 1, LOGIN = "deja" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.CreateAdminAsync(Dto));

        Assert.StartsWith("Des comptes existent déjà", ex.Message);
        Assert.Single(_uow.GetRepo<T_RESPONSABLE>().Items);
    }

    [Fact]
    public async Task Database_without_any_role_is_refused()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.CreateAdminAsync(Dto));

        Assert.Equal(Missing, ex.Message);
        Assert.Empty(_uow.GetRepo<T_RESPONSABLE>().Items);
    }

    [Fact]
    public async Task Weak_password_is_still_refused_before_any_role_lookup()
    {
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        await Assert.ThrowsAnyAsync<Exception>(() => _auth.CreateAdminAsync(new AdminSetupDto("Admin", "admin", "weak", null)));
        Assert.Empty(_uow.GetRepo<T_RESPONSABLE>().Items);
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
