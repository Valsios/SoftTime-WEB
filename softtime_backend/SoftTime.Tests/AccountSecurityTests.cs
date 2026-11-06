using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using AutoMapper;

using SoftTime.Api.Security;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Security;
using SoftTime.Infrastructure.Services;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Sécurité des comptes (convention §9.1) : verrouillage après échecs, jetons de rafraîchissement rotatifs et
/// révocables, détection de réutilisation, hachage BCrypt et clé JWT obligatoire. Mots de passe factices.
/// </summary>
public class AccountSecurityTests
{
    private const string Password = "FAKE-Pwd-123!";
    private const string OtherPassword = "FAKE-Other-456!";

    private readonly SnapshotInMemoryUnitOfWork _uow = new();
    private readonly AccountSecurityOptions _options = new() { MaxFailedLoginAttempts = 3, LockoutMinutes = 15, RefreshTokenDays = 7 };
    private readonly AuthService _sut;

    public AccountSecurityTests()
    {
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        AddUser(1, 1, "admin", new PasswordHasher<Responsable>());
        AddUser(2, 2, "agent", new PasswordHasher<Responsable>());
        _sut = CreateAuth(new PasswordHasher<Responsable>());
    }

    // ---------- Verrouillage ----------

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsRefreshToken()
    {
        // Act
        var response = await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(response.RefreshToken));
        var stored = Assert.Single(Tokens());
        Assert.Equal(RefreshTokenService.Hash(response.RefreshToken!), stored.TokenHash);
        Assert.NotEqual(response.RefreshToken, stored.TokenHash);
    }

    [Fact]
    public async Task LoginAsync_TooManyWrongPasswords_LocksAccountEvenForCorrectPassword()
    {
        // Arrange
        for (var i = 0; i < _options.MaxFailedLoginAttempts; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.LoginAsync(new LoginRequest("agent", "wrong")));

        // Act
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.LoginAsync(new LoginRequest("agent", Password)));

        // Assert
        Assert.StartsWith("Compte temporairement verrouillé", ex.Message);
        Assert.True(User(2).VerrouilleJusqua > DateTime.UtcNow);
        Assert.Empty(Tokens());
    }

    [Fact]
    public async Task LoginAsync_SuccessAfterFailures_ResetsFailureCounter()
    {
        // Arrange
        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.LoginAsync(new LoginRequest("agent", "wrong")));
        Assert.Equal(1, User(2).TentativesEchouees);

        // Act
        await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        Assert.Equal(0, User(2).TentativesEchouees);
        Assert.Null(User(2).VerrouilleJusqua);
    }

    [Fact]
    public async Task LoginAsync_ExpiredLock_AllowsLoginAgain()
    {
        // Arrange
        User(2).VerrouilleJusqua = DateTime.UtcNow.AddMinutes(-1);

        // Act
        var response = await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        Assert.Equal(2, response.UserId);
        Assert.Null(User(2).VerrouilleJusqua);
    }

    [Fact]
    public async Task LoginAsync_UnknownLogin_ThrowsSameMessageAsWrongPassword()
    {
        // Act
        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.LoginAsync(new LoginRequest("inconnu", Password)));
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.LoginAsync(new LoginRequest("agent", "wrong")));

        // Assert
        Assert.Equal(AuthService.InvalidCredentialsMessage, unknown.Message);
        Assert.Equal(unknown.Message, wrong.Message);
    }

    // ---------- Jetons de rafraîchissement ----------

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesAndRevokesPreviousToken()
    {
        // Arrange
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Act
        var refreshed = await _sut.RefreshAsync(login.RefreshToken);

        // Assert
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal("FAKE_TOKEN", refreshed.Token);
        var previous = StoredToken(login.RefreshToken!);
        var next = StoredToken(refreshed.RefreshToken!);
        Assert.NotNull(previous.DateRevocation);
        Assert.Equal(next.TokenHash, previous.RemplacePar);
        Assert.Equal(previous.Famille, next.Famille);
        Assert.Null(next.DateRevocation);
    }

    [Fact]
    public async Task RefreshAsync_TokenJustRotated_IsRefusedWithoutClosingTheNewSession()
    {
        // Arrange : deux onglets présentent le même jeton à quelques secondes d'intervalle.
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        var refreshed = await _sut.RefreshAsync(login.RefreshToken);

        // Act
        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshAsync(login.RefreshToken));

        // Assert
        Assert.Null(StoredToken(refreshed.RefreshToken!).DateRevocation);
    }

    [Fact]
    public async Task RefreshAsync_RotatedTokenReusedAfterGracePeriod_RevokesAllSessions()
    {
        // Arrange
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        var refreshed = await _sut.RefreshAsync(login.RefreshToken);
        StoredToken(login.RefreshToken!).DateRevocation = DateTime.UtcNow.AddMinutes(-5);

        // Act
        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshAsync(login.RefreshToken));

        // Assert
        Assert.NotNull(StoredToken(refreshed.RefreshToken!).DateRevocation);
        Assert.All(Tokens(), t => Assert.NotNull(t.DateRevocation));
    }

    [Fact]
    public async Task RefreshAsync_TokenRevokedByLogout_RevokesOtherSessionsOfTheAccount()
    {
        // Arrange
        var first = await _sut.LoginAsync(new LoginRequest("agent", Password));
        var second = await _sut.LoginAsync(new LoginRequest("agent", Password));
        await _sut.LogoutAsync(first.RefreshToken);

        // Act
        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshAsync(first.RefreshToken));

        // Assert
        Assert.NotNull(StoredToken(second.RefreshToken!).DateRevocation);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ThrowsUnauthorized()
    {
        // Arrange
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        StoredToken(login.RefreshToken!).DateExpiration = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshAsync(login.RefreshToken));

        // Assert
        Assert.Equal(RefreshTokenService.InvalidTokenMessage, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jeton-inconnu")]
    public async Task RefreshAsync_MissingOrUnknownToken_ThrowsUnauthorized(string? token)
    {
        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshAsync(token));

        // Assert
        Assert.Equal(RefreshTokenService.InvalidTokenMessage, ex.Message);
    }

    [Fact]
    public async Task RefreshAsync_LockedAccount_ThrowsUnauthorized()
    {
        // Arrange
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        User(2).VerrouilleJusqua = DateTime.UtcNow.AddMinutes(10);

        // Act
        var act = () => _sut.RefreshAsync(login.RefreshToken);

        // Assert
        await Assert.ThrowsAsync<UnauthorizedException>(act);
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_DoesNothing()
    {
        // Arrange
        await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Act
        await _sut.LogoutAsync("jeton-inconnu");

        // Assert
        Assert.Null(Assert.Single(Tokens()).DateRevocation);
    }

    [Fact]
    public async Task SaveUserAsync_PasswordChanged_UnlocksAccountAndClosesSessions()
    {
        // Arrange
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        User(2).TentativesEchouees = 2;
        User(2).VerrouilleJusqua = DateTime.UtcNow.AddMinutes(10);

        // Act
        await _sut.SaveUserAsync(1, new UserDto(2, 2, "Agent", "agent", null, OtherPassword));

        // Assert
        Assert.Equal(0, User(2).TentativesEchouees);
        Assert.Null(User(2).VerrouilleJusqua);
        Assert.NotNull(StoredToken(login.RefreshToken!).DateRevocation);
    }

    [Fact]
    public async Task SaveUserAsync_SamePasswordWithLegacyHash_KeepsSessionsAndUpgradesHash()
    {
        // Arrange : compte haché par l'ancien hacheur, service configuré avec BCrypt.
        var sut = CreateAuth(new BcryptPasswordHasher());
        var login = await sut.LoginAsync(new LoginRequest("admin", Password));
        AddUser(3, 2, "ancien", new PasswordHasher<Responsable>());

        // Act
        await sut.SaveUserAsync(1, new UserDto(3, 2, "Ancien", "ancien", null, Password));

        // Assert
        Assert.True(BcryptPasswordHasher.IsBcrypt(User(3).Password));
        Assert.Null(StoredToken(login.RefreshToken!).DateRevocation);
    }

    // ---------- BCrypt ----------

    [Fact]
    public void HashPassword_AnyPassword_ProducesVerifiableBcryptHash()
    {
        // Arrange
        var sut = new BcryptPasswordHasher();

        // Act
        var hash = sut.HashPassword(new Responsable(), Password);

        // Assert
        Assert.True(BcryptPasswordHasher.IsBcrypt(hash));
        Assert.Equal(PasswordVerificationResult.Success, sut.VerifyHashedPassword(new Responsable(), hash, Password));
        Assert.Equal(PasswordVerificationResult.Failed, sut.VerifyHashedPassword(new Responsable(), hash, "wrong"));
    }

    [Fact]
    public void VerifyHashedPassword_IdentityHash_ReturnsRehashNeeded()
    {
        // Arrange
        var sut = new BcryptPasswordHasher();
        var identityHash = new PasswordHasher<Responsable>().HashPassword(new Responsable(), Password);

        // Act
        var result = sut.VerifyHashedPassword(new Responsable(), identityHash, Password);

        // Assert
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
        Assert.Equal(PasswordVerificationResult.Failed, sut.VerifyHashedPassword(new Responsable(), identityHash, "wrong"));
    }

    [Fact]
    public async Task LoginAsync_IdentityHashWithBcryptHasher_UpgradesStoredHashToBcrypt()
    {
        // Arrange
        var sut = CreateAuth(new BcryptPasswordHasher());

        // Act
        await sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        Assert.True(BcryptPasswordHasher.IsBcrypt(User(2).Password));
        Assert.Equal(PasswordVerificationResult.Success, new BcryptPasswordHasher().VerifyHashedPassword(User(2), User(2).Password!, Password));
    }

    // ---------- Clé JWT ----------

    [Theory]
    [InlineData("")]
    [InlineData("trop-courte")]
    [InlineData(JwtOptions.PublishedDevelopmentKey)]
    public void Validate_UnsafeJwtKey_Fails(string key)
    {
        // Arrange
        var sut = new JwtOptionsValidator();

        // Act
        var result = sut.Validate(null, new JwtOptions { Key = key, Issuer = "SoftTime", Audience = "SoftTime.Web" });

        // Assert
        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_StrongJwtKey_Succeeds()
    {
        // Arrange
        var sut = new JwtOptionsValidator();

        // Act
        var result = sut.Validate(null, new JwtOptions { Key = new string('k', 48), Issuer = "SoftTime", Audience = "SoftTime.Web" });

        // Assert
        Assert.True(result.Succeeded);
    }

    private AuthService CreateAuth(IPasswordHasher<Responsable> hasher)
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        var options = Options.Create(_options);
        return new AuthService(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), TestRights.Service(_uow), hasher,
            new RefreshTokenService(_uow, options), options);
    }

    private void AddUser(decimal id, decimal role, string login, IPasswordHasher<Responsable> hasher)
    {
        var user = new Responsable { IdResponsable = id, IdRole = role, Login = login, NomResponsable = login };
        user.Password = hasher.HashPassword(user, Password);
        _uow.GetRepo<Responsable>().Items.Add(user);
    }

    private Responsable User(decimal id) => _uow.GetRepo<Responsable>().Items.Single(u => u.IdResponsable == id);

    private List<RefreshTokenRecord> Tokens() => _uow.GetRepo<RefreshTokenRecord>().Items;

    private RefreshTokenRecord StoredToken(string refreshToken)
        => Tokens().Single(t => t.TokenHash == RefreshTokenService.Hash(refreshToken));

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
