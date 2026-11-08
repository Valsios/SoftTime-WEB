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
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _uow.Get<DroitAcces>().Items.Add(new DroitAcces { IdDroit = 1, Droit = "Bases" });

        // Act
        await _auth.CreateAdminAsync(Dto);

        // Assert
        var created = _uow.Get<Responsable>().Items.Should().ContainSingle().Which;
        created.IdRole.Should().Be(1);
        _uow.Get<DroitRole>().Items.Should().Contain(d => d.IdRole == 1 && d.IdDroit == 1);
    }

    [Fact]
    public async Task Administrator_role_with_another_identifier_is_refused_and_no_account_is_created()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Administrateur" });

        // Act
        Func<Task> act = () => _auth.CreateAdminAsync(Dto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be(Missing);
        _uow.Get<Responsable>().Items.Should().BeEmpty();
        _uow.Get<DroitRole>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_account_is_still_refused()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 5, IdRole = 1, Login = "deja" });

        // Act
        Func<Task> act = () => _auth.CreateAdminAsync(Dto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().StartWith("Des comptes existent déjà");
        _uow.Get<Responsable>().Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Database_without_any_role_is_refused()
    {
        // Act
        Func<Task> act = () => _auth.CreateAdminAsync(Dto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Be(Missing);
        _uow.Get<Responsable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Weak_password_is_still_refused_before_any_role_lookup()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });

        // Act
        Func<Task> act = () => _auth.CreateAdminAsync(new AdminSetupDto("Admin", "admin", "weak", null));

        // Assert
        await act.Should().ThrowAsync<Exception>();
        _uow.Get<Responsable>().Items.Should().BeEmpty();
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
