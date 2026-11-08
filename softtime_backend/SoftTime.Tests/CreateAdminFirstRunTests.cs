using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Premier démarrage : create-admin exige le rôle d'identifiant 1, seul rôle administrateur.</summary>
public class CreateAdminFirstRunTests
{
    private const string Missing = "Le rôle Administrateur (identifiant 1) est introuvable : exécutez le script de création de la base.";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AuthServices _auth;

    public CreateAdminFirstRunTests()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
    }

    private static AdminSetupDto Dto => new("Admin Test", "admin", "ValidP@ss123!", null);

    [Fact]
    public async Task Fresh_database_with_role_1_creates_an_account_with_IDROLE_1()
    {
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _uow.Get<DroitAcces>().Items.Add(new DroitAcces { IdDroit = 1, Droit = "Bases" });

        await _auth.CreateAdminAsync(Dto);

        var created = Assert.Single(_uow.Get<Responsable>().Items);
        Assert.Equal(1, created.IdRole);
        Assert.Contains(_uow.Get<DroitRole>().Items, d => d.IdRole == 1 && d.IdDroit == 1);
    }

    [Fact]
    public async Task Administrator_role_with_another_identifier_is_refused_and_no_account_is_created()
    {
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Administrateur" });

        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.CreateAdminAsync(Dto));

        Assert.Equal(Missing, ex.Message);
        Assert.Empty(_uow.Get<Responsable>().Items);
        Assert.Empty(_uow.Get<DroitRole>().Items);
    }

    [Fact]
    public async Task Existing_account_is_still_refused()
    {
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 5, IdRole = 1, Login = "deja" });

        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.CreateAdminAsync(Dto));

        Assert.StartsWith("Des comptes existent déjà", ex.Message);
        Assert.Single(_uow.Get<Responsable>().Items);
    }

    [Fact]
    public async Task Database_without_any_role_is_refused()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.CreateAdminAsync(Dto));

        Assert.Equal(Missing, ex.Message);
        Assert.Empty(_uow.Get<Responsable>().Items);
    }

    [Fact]
    public async Task Weak_password_is_still_refused_before_any_role_lookup()
    {
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        await Assert.ThrowsAnyAsync<Exception>(() => _auth.CreateAdminAsync(new AdminSetupDto("Admin", "admin", "weak", null)));
        Assert.Empty(_uow.Get<Responsable>().Items);
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
