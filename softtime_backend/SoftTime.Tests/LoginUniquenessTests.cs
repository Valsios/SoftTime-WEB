using AutoMapper;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Recherche du compte à la connexion (une seule requête, sans casse ni espaces, jamais un compte au hasard
/// parmi des doublons), unicité des logins et création initiale de l'administrateur sous verrou exclusif.
/// Mots de passe factices.
/// </summary>
public class LoginUniquenessTests
{
    private const string Password = "FAKE-Pwd-123!";
    private const string AdminPassword = "FAKE-Admin-789!";

    private readonly LockingUnitOfWork _uow = new();
    private readonly PasswordHasher<Responsable> _hasher = new();

    public LoginUniquenessTests()
    {
        _uow.Inner.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Inner.GetRepo<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
    }

    // ---------- Clé de login ----------

    [Theory]
    [InlineData("agent", "AGENT")]
    [InlineData("  Agent ", "AGENT")]
    [InlineData(null, "")]
    public void Normalize_TrimsAndUppercases(string? login, string expected)
    {
        // Act
        var key = LoginKey.Normalize(login);

        // Assert
        Assert.Equal(expected, key);
    }

    // ---------- Connexion ----------

    [Theory]
    [InlineData("agent")]
    [InlineData("AGENT")]
    [InlineData("  Agent  ")]
    public async Task LoginAsync_LoginDiffersByCaseOrSpaces_FindsTheAccount(string typed)
    {
        // Arrange
        AddUser(2, 2, " Agent");
        var sut = CreateAuth();

        // Act
        var response = await sut.LoginAsync(new LoginRequest(typed, Password));

        // Assert
        Assert.Equal(2, response.UserId);
    }

    [Fact]
    public async Task LoginAsync_LegacyDuplicates_ExactLoginWins()
    {
        // Arrange : ancienne base où l'index unique de la migration 005 n'a pas pu être posé.
        AddUser(2, 2, "AGENT");
        AddUser(3, 2, "agent");
        var sut = CreateAuth();

        // Act
        var response = await sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        Assert.Equal(3, response.UserId);
    }

    [Fact]
    public async Task LoginAsync_LegacyDuplicatesWithoutExactMatch_RefusesInsteadOfPickingOne()
    {
        // Arrange
        AddUser(2, 2, "AGENT");
        AddUser(3, 2, "Agent");
        var sut = CreateAuth();

        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => sut.LoginAsync(new LoginRequest("agent", Password)));

        // Assert
        Assert.Equal("Identifiants invalides.", ex.Message);
        Assert.Empty(_uow.Inner.GetRepo<RefreshTokenRecord>().Items);
    }

    // ---------- Unicité à l'enregistrement ----------

    [Theory]
    [InlineData("AGENT")]
    [InlineData(" agent ")]
    public async Task SaveUserAsync_NewUserWithLoginTakenIgnoringCaseAndSpaces_Throws(string login)
    {
        // Arrange
        AddUser(2, 2, "agent");
        var sut = CreateAuth();

        // Act
        var ex = await Assert.ThrowsAsync<FieldValidationException>(() =>
            sut.SaveUserAsync(new UserDto(0, 2, "Autre", login, null, Password)));

        // Assert
        Assert.Equal("login", ex.Field);
        Assert.Equal(LoginKey.TakenMessage, ex.Message);
        Assert.Single(_uow.Inner.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUserKeepsOwnLoginWithOtherCase_IsAccepted()
    {
        // Arrange
        AddUser(2, 2, "agent");
        var sut = CreateAuth();

        // Act
        var saved = await sut.SaveUserAsync(new UserDto(2, 2, "Agent", "AGENT", null, null));

        // Assert
        Assert.Equal("AGENT", saved.Login);
    }

    // ---------- Création initiale de l'administrateur ----------

    [Fact]
    public async Task CreateAdminAsync_RunsUnderExclusiveLock()
    {
        // Arrange
        var sut = CreateAuth();

        // Act
        await sut.CreateAdminAsync(new AdminSetupDto("Admin", "admin", AdminPassword, null));

        // Assert
        Assert.Equal(new[] { "SoftTime.CreateAdmin" }, _uow.LockedResources);
        Assert.Single(_uow.Inner.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task CreateAdminAsync_AccountCreatedConcurrentlyBeforeLock_IsRefusedUnderLock()
    {
        // Arrange : une autre requête crée son compte entre la première vérification et la prise du verrou.
        _uow.BeforeLockedAction = () => AddUser(5, 1, "intrus");
        var sut = CreateAuth();

        // Act
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            sut.CreateAdminAsync(new AdminSetupDto("Admin", "admin", AdminPassword, null)));

        // Assert
        Assert.StartsWith("Des comptes existent déjà", ex.Message);
        var only = Assert.Single(_uow.Inner.GetRepo<Responsable>().Items);
        Assert.Equal("intrus", only.Login);
    }

    [Fact]
    public async Task CreateAdminAsync_TwoSimultaneousCalls_CreateOnlyOneAdministrator()
    {
        // Arrange
        var first = CreateAuth();
        var second = CreateAuth();

        // Act
        var results = await Task.WhenAll(
            Capture(() => first.CreateAdminAsync(new AdminSetupDto("Admin A", "admin-a", AdminPassword, null))),
            Capture(() => second.CreateAdminAsync(new AdminSetupDto("Admin B", "admin-b", AdminPassword, null))));

        // Assert
        Assert.Single(_uow.Inner.GetRepo<Responsable>().Items);
        Assert.Single(results, r => r is null);
        Assert.Single(results, r => r is DomainException);
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private AuthServices CreateAuth()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        var options = Options.Create(new AccountSecurityOptions());
        return new AuthServices(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), TestRights.Service(_uow), _hasher,
            new RefreshTokenService(_uow, options), options);
    }

    private void AddUser(decimal id, decimal role, string login)
    {
        var user = new Responsable { IdResponsable = id, IdRole = role, Login = login, NomResponsable = login };
        user.Password = _hasher.HashPassword(user, Password);
        _uow.Inner.GetRepo<Responsable>().Items.Add(user);
    }

    /// <summary>Unité de travail en mémoire dont le verrou exclusif sérialise réellement les appels.</summary>
    private sealed class LockingUnitOfWork : IUnitOfWork
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SnapshotInMemoryUnitOfWork Inner { get; } = new();
        public List<string> LockedResources { get; } = new();
        public Action? BeforeLockedAction { get; set; }

        public IRepository<T> Repository<T>() where T : class => Inner.Repository<T>();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Inner.SaveChangesAsync(cancellationToken);
        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Inner.ExecuteSqlAsync(sql, cancellationToken);

        public async Task<TResult> RunExclusiveAsync<TResult>(
            string resource,
            Func<CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                LockedResources.Add(resource);
                BeforeLockedAction?.Invoke();
                BeforeLockedAction = null;
                // Laisse l'autre appel avancer jusqu'au verrou pendant que celui-ci le détient.
                await Task.Yield();
                return await action(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }
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
