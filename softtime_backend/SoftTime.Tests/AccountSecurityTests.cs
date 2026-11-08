using AutoMapper;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

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

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AccountSecurityOptions _options = new() { MaxFailedLoginAttempts = 3, LockoutMinutes = 15, RefreshTokenDays = 7 };
    private readonly AuthServices _sut;

    public AccountSecurityTests()
    {
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
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
        string.IsNullOrWhiteSpace(response.RefreshToken).Should().BeFalse();
        var stored = Tokens().Should().ContainSingle().Which;
        stored.TokenHash.Should().Be(RefreshTokenService.Hash(response.RefreshToken!));
        stored.TokenHash.Should().NotBe(response.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_TooManyWrongPasswords_LocksAccountEvenForCorrectPassword()
    {
        // Arrange
        for (var i = 0; i < _options.MaxFailedLoginAttempts; i++)
            await FluentActions.Awaiting(() => _sut.LoginAsync(new LoginRequest("agent", "wrong"))).Should().ThrowExactlyAsync<UnauthorizedException>();

        // Act
        var ex = (await FluentActions.Awaiting(() => _sut.LoginAsync(new LoginRequest("agent", Password))).Should().ThrowExactlyAsync<ForbiddenException>()).Which;

        // Assert
        ex.Message.Should().StartWith("Compte temporairement verrouillé");
        (User(2).VerrouilleJusqua > DateTime.UtcNow).Should().BeTrue();
        Tokens().Should().BeEmpty();
    }

    [Fact]
    public async Task LoginAsync_SuccessAfterFailures_ResetsFailureCounter()
    {
        // Arrange
        await FluentActions.Awaiting(() => _sut.LoginAsync(new LoginRequest("agent", "wrong"))).Should().ThrowExactlyAsync<UnauthorizedException>();
        User(2).TentativesEchouees.Should().Be(1);

        // Act
        await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        User(2).TentativesEchouees.Should().Be(0);
        User(2).VerrouilleJusqua.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_ExpiredLock_AllowsLoginAgain()
    {
        // Arrange
        User(2).VerrouilleJusqua = DateTime.UtcNow.AddMinutes(-1);

        // Act
        var response = await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        response.UserId.Should().Be(2);
        User(2).VerrouilleJusqua.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_UnknownLogin_ThrowsSameMessageAsWrongPassword()
    {
        // Act
        var unknown = (await FluentActions.Awaiting(() => _sut.LoginAsync(new LoginRequest("inconnu", Password))).Should().ThrowExactlyAsync<UnauthorizedException>()).Which;
        var wrong = (await FluentActions.Awaiting(() => _sut.LoginAsync(new LoginRequest("agent", "wrong"))).Should().ThrowExactlyAsync<UnauthorizedException>()).Which;

        // Assert
        unknown.Message.Should().Be(AuthenticationService.InvalidCredentialsMessage);
        wrong.Message.Should().Be(unknown.Message);
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
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);
        refreshed.Token.Should().Be("FAKE_TOKEN");
        var previous = StoredToken(login.RefreshToken!);
        var next = StoredToken(refreshed.RefreshToken!);
        previous.DateRevocation.Should().NotBeNull();
        previous.RemplacePar.Should().Be(next.TokenHash);
        next.Famille.Should().Be(previous.Famille);
        next.DateRevocation.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_TokenJustRotated_IsRefusedWithoutClosingTheNewSession()
    {
        // Arrange : deux onglets présentent le même jeton à quelques secondes d'intervalle.
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        var refreshed = await _sut.RefreshAsync(login.RefreshToken);

        // Act
        await FluentActions.Awaiting(() => _sut.RefreshAsync(login.RefreshToken)).Should().ThrowExactlyAsync<UnauthorizedException>();

        // Assert
        StoredToken(refreshed.RefreshToken!).DateRevocation.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_RotatedTokenReusedAfterGracePeriod_RevokesAllSessions()
    {
        // Arrange
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        var refreshed = await _sut.RefreshAsync(login.RefreshToken);
        StoredToken(login.RefreshToken!).DateRevocation = DateTime.UtcNow.AddMinutes(-5);

        // Act
        await FluentActions.Awaiting(() => _sut.RefreshAsync(login.RefreshToken)).Should().ThrowExactlyAsync<UnauthorizedException>();

        // Assert
        StoredToken(refreshed.RefreshToken!).DateRevocation.Should().NotBeNull();
        Tokens().Should().AllSatisfy(t => t.DateRevocation.Should().NotBeNull());
    }

    [Fact]
    public async Task RefreshAsync_TokenRevokedByLogout_RevokesOtherSessionsOfTheAccount()
    {
        // Arrange
        var first = await _sut.LoginAsync(new LoginRequest("agent", Password));
        var second = await _sut.LoginAsync(new LoginRequest("agent", Password));
        await _sut.LogoutAsync(first.RefreshToken);

        // Act
        await FluentActions.Awaiting(() => _sut.RefreshAsync(first.RefreshToken)).Should().ThrowExactlyAsync<UnauthorizedException>();

        // Assert
        StoredToken(second.RefreshToken!).DateRevocation.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ThrowsUnauthorized()
    {
        // Arrange
        var login = await _sut.LoginAsync(new LoginRequest("agent", Password));
        StoredToken(login.RefreshToken!).DateExpiration = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var ex = (await FluentActions.Awaiting(() => _sut.RefreshAsync(login.RefreshToken)).Should().ThrowExactlyAsync<UnauthorizedException>()).Which;

        // Assert
        ex.Message.Should().Be(RefreshTokenService.InvalidTokenMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jeton-inconnu")]
    public async Task RefreshAsync_MissingOrUnknownToken_ThrowsUnauthorized(string? token)
    {
        // Act
        var ex = (await FluentActions.Awaiting(() => _sut.RefreshAsync(token)).Should().ThrowExactlyAsync<UnauthorizedException>()).Which;

        // Assert
        ex.Message.Should().Be(RefreshTokenService.InvalidTokenMessage);
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
        await FluentActions.Awaiting(act).Should().ThrowExactlyAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_DoesNothing()
    {
        // Arrange
        await _sut.LoginAsync(new LoginRequest("agent", Password));

        // Act
        await _sut.LogoutAsync("jeton-inconnu");

        // Assert
        Tokens().Should().ContainSingle().Which.DateRevocation.Should().BeNull();
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
        User(2).TentativesEchouees.Should().Be(0);
        User(2).VerrouilleJusqua.Should().BeNull();
        StoredToken(login.RefreshToken!).DateRevocation.Should().NotBeNull();
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
        BcryptPasswordHasher.IsBcrypt(User(3).Password).Should().BeTrue();
        StoredToken(login.RefreshToken!).DateRevocation.Should().BeNull();
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
        BcryptPasswordHasher.IsBcrypt(hash).Should().BeTrue();
        sut.VerifyHashedPassword(new Responsable(), hash, Password).Should().Be(PasswordVerificationResult.Success);
        sut.VerifyHashedPassword(new Responsable(), hash, "wrong").Should().Be(PasswordVerificationResult.Failed);
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
        result.Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
        sut.VerifyHashedPassword(new Responsable(), identityHash, "wrong").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public async Task LoginAsync_IdentityHashWithBcryptHasher_UpgradesStoredHashToBcrypt()
    {
        // Arrange
        var sut = CreateAuth(new BcryptPasswordHasher());

        // Act
        await sut.LoginAsync(new LoginRequest("agent", Password));

        // Assert
        BcryptPasswordHasher.IsBcrypt(User(2).Password).Should().BeTrue();
        new BcryptPasswordHasher().VerifyHashedPassword(User(2), User(2).Password!, Password).Should().Be(PasswordVerificationResult.Success);
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
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_StrongJwtKey_Succeeds()
    {
        // Arrange
        var sut = new JwtOptionsValidator();

        // Act
        var result = sut.Validate(null, new JwtOptions { Key = new string('k', 48), Issuer = "SoftTime", Audience = "SoftTime.Web" });

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    private AuthServices CreateAuth(IPasswordHasher<Responsable> hasher)
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        var options = Options.Create(_options);
        return new AuthServices(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), TestRights.Service(_uow), hasher,
            new RefreshTokenService(_uow, options), options);
    }

    private void AddUser(decimal id, decimal role, string login, IPasswordHasher<Responsable> hasher)
    {
        var user = new Responsable { IdResponsable = id, IdRole = role, Login = login, NomResponsable = login };
        user.Password = hasher.HashPassword(user, Password);
        _uow.Get<Responsable>().Items.Add(user);
    }

    private Responsable User(decimal id) => _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == id);

    private List<RefreshTokenRecord> Tokens() => _uow.Get<RefreshTokenRecord>().Items;

    private RefreshTokenRecord StoredToken(string refreshToken)
        => Tokens().Single(t => t.TokenHash == RefreshTokenService.Hash(refreshToken));

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
