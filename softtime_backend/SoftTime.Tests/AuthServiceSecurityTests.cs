using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class AuthServiceSecurityTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;
    private readonly IEmailService _email;
    private readonly PasswordHasher<Responsable> _hasher;
    private readonly AuthServices _auth;

    public AuthServiceSecurityTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = ServiceMocks.TokenService().Object;

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = ServiceMocks.EmailService().Object;
        _hasher = new PasswordHasher<Responsable>();
        _auth = new AuthServices(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

        // Seed default role
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
    }

    [Fact]
    public async Task CreateAdminAsync_HashesPassword_NeverStoresPlainText()
    {
        // Arrange
        var result = await _auth.CreateAdminAsync(new AdminSetupDto("Admin User", "admin", "AdminSecret123!", "admin@softtime.local"));

        // Act
        var adminUser = _uow.Get<Responsable>().Items.Single(u => u.Login == "admin");

        // Assert
        adminUser.Password.Should().NotBeNull();
        adminUser.Password.Should().NotBe("AdminSecret123!");
        PasswordHashClassifier.IsHashedPassword(adminUser.Password).Should().BeTrue();
        var verifyResult = _hasher.VerifyHashedPassword(adminUser, adminUser.Password, "AdminSecret123!");
        verifyResult.Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_HashesPasswordAndDoesNotLeakInDto()
    {
        // Arrange
        var userDto = new UserDto(0, 2, "Test User", "testuser", "M001", "UserPassword456!")
        {
            Email = "test@softtime.local"
        };
        var createdDto = await _auth.SaveUserAsync(userDto);

        // Act
        var savedEntity = _uow.Get<Responsable>().Items.Single(u => u.Login == "testuser");

        // Assert
        savedEntity.Password.Should().NotBeNull();
        savedEntity.Password.Should().NotBe("UserPassword456!");
        PasswordHashClassifier.IsHashedPassword(savedEntity.Password).Should().BeTrue();
        // Returned DTO must NEVER contain password or hash
        createdDto.Password.Should().BeNull();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutPassword_ThrowsFieldValidation()
    {
        // Arrange
        var userDto = new UserDto(0, 2, "No Pwd", "nopwd", "M002", "");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(userDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<FieldValidationException>()).Which;
        ex.Field.Should().Be("password");
        _uow.Get<Responsable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithWeakPassword_ThrowsAndDoesNotCreateUser()
    {
        // Arrange
        var userDto = new UserDto(0, 2, "Weak User", "weakuser", "M010", "password");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(userDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<FieldValidationException>()).Which;
        ex.Field.Should().Be("password");
        ex.Message.Should().Contain("règles de sécurité");
        _uow.Get<Responsable>().Items.Where(u => u.Login == "weakuser").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutName_ThrowsFieldValidation()
    {
        // Arrange
        var userDto = new UserDto(0, 2, "   ", "sansnom", null, "StrongPassword123!");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(userDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<FieldValidationException>()).Which;
        ex.Field.Should().Be("nom");
        _uow.Get<Responsable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutLogin_ThrowsFieldValidation()
    {
        // Arrange
        var userDto = new UserDto(0, 2, "Sans Login", "   ", null, "StrongPassword123!");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(userDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<FieldValidationException>()).Which;
        ex.Field.Should().Be("login");
        _uow.Get<Responsable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutRole_ThrowsFieldValidation()
    {
        // Arrange
        var userDto = new UserDto(0, 0, "Sans Rôle", "sansrole", null, "StrongPassword123!");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(userDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<FieldValidationException>()).Which;
        ex.Field.Should().Be("roleId");
        _uow.Get<Responsable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithDuplicateLogin_ThrowsFieldValidation()
    {
        // Arrange
        _uow.Get<Responsable>().Items.Add(new Responsable
        {
            IdResponsable = 50,
            IdRole = 2,
            NomResponsable = "Existante",
            Login = "Alice",
            Password = _hasher.HashPassword(new Responsable(), "ExistingSecret123!")
        });
        var userDto = new UserDto(0, 2, "Doublon", "  alice ", null, "StrongPassword123!");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(userDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<FieldValidationException>()).Which;
        ex.Field.Should().Be("login");
        _uow.Get<Responsable>().Items.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveUserAsync_ExistingLogin_AllowsProfileUpdateEvenIfLoginIsAlreadyDuplicated()
    {
        // Arrange
        var hash = _hasher.HashPassword(new Responsable(), "Secret123!");
        _uow.Get<Responsable>().Items.AddRange(new[]
        {
            new Responsable { IdResponsable = 60, IdRole = 2, NomResponsable = "Premier", Login = "shared", Password = hash },
            new Responsable { IdResponsable = 61, IdRole = 2, NomResponsable = "Second", Login = "shared", Password = hash },
        });
        var updateDto = new UserDto(60, 2, "Premier Renommé", "shared", null, null);

        // Act
        var result = await _auth.SaveUserAsync(updateDto);

        // Assert
        result.Nom.Should().Be("Premier Renommé");
        _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 60).Password.Should().Be(hash);
    }

    [Fact]
    public async Task CreateAdminAsync_WithWeakPassword_ThrowsAndDoesNotCreateAccount()
    {
        // Act
        Func<Task> act = () =>
            _auth.CreateAdminAsync(new AdminSetupDto("Admin User", "admin", "weakpass", null));

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        ex.Message.Should().Contain("règles de sécurité");
        _uow.Get<Responsable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_WithWeakNewPassword_ThrowsAndPreservesExistingHash()
    {
        // Arrange
        var initialHash = _hasher.HashPassword(new Responsable(), "OldSecret!");
        var user = new Responsable
        {
            IdResponsable = 12,
            IdRole = 2,
            NomResponsable = "User To Update",
            Login = "weakupdate",
            Password = initialHash
        };
        _uow.Get<Responsable>().Items.Add(user);
        var updateDto = new UserDto(12, 2, "User To Update", "weakupdate", "M011", "azerty");

        // Act
        Func<Task> act = () => _auth.SaveUserAsync(updateDto);

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<FieldValidationException>()).Which;
        ex.Field.Should().Be("password");
        var unchanged = _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 12);
        unchanged.Password.Should().Be(initialHash);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_PreservesPasswordWhenNotProvided()
    {
        // Arrange
        var initialHash = _hasher.HashPassword(new Responsable(), "OriginalSecret!");
        var user = new Responsable
        {
            IdResponsable = 10,
            IdRole = 2,
            NomResponsable = "Existing User",
            Login = "existing",
            Password = initialHash
        };
        _uow.Get<Responsable>().Items.Add(user);
        // Update profile with null / empty password
        var updateDto = new UserDto(10, 2, "Existing User Renamed", "existing", "M003", null);
        var resultDto = await _auth.SaveUserAsync(updateDto);

        // Act
        var updatedEntity = _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 10);

        // Assert
        updatedEntity.NomResponsable.Should().Be("Existing User Renamed");
        updatedEntity.Password.Should().Be(initialHash);
        resultDto.Password.Should().BeNull();
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_UpdatesPasswordWithNewHash()
    {
        // Arrange
        var initialHash = _hasher.HashPassword(new Responsable(), "OldSecret!");
        var user = new Responsable
        {
            IdResponsable = 11,
            IdRole = 2,
            NomResponsable = "User To Update",
            Login = "toupdate",
            Password = initialHash
        };
        _uow.Get<Responsable>().Items.Add(user);
        var updateDto = new UserDto(11, 2, "User To Update", "toupdate", "M004", "BrandNewSecret789!");
        var resultDto = await _auth.SaveUserAsync(updateDto);

        // Act
        var updatedEntity = _uow.Get<Responsable>().Items.Single(u => u.IdResponsable == 11);

        // Assert
        updatedEntity.Password.Should().NotBe(initialHash);
        PasswordHashClassifier.IsHashedPassword(updatedEntity.Password).Should().BeTrue();
        var verify = _hasher.VerifyHashedPassword(updatedEntity, updatedEntity.Password!, "BrandNewSecret789!");
        verify.Should().Be(PasswordVerificationResult.Success);
        resultDto.Password.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithValidHashedPassword_Succeeds()
    {
        // Arrange
        var user = new Responsable
        {
            IdResponsable = 1,
            IdRole = 1,
            NomResponsable = "Admin",
            Login = "admin",
            Password = _hasher.HashPassword(new Responsable(), "CorrectPassword123")
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        var response = await _auth.LoginAsync(new LoginRequest("admin", "CorrectPassword123"));

        // Assert
        response.Should().NotBeNull();
        response.Login.Should().Be("admin");
        response.Token.Should().Be("FAKE_JWT_TOKEN_1_admin");
    }

    [Fact]
    public async Task LoginAsync_WithIncorrectPassword_ThrowsUnauthorized()
    {
        // Arrange
        var user = new Responsable
        {
            IdResponsable = 1,
            IdRole = 1,
            Login = "admin",
            Password = _hasher.HashPassword(new Responsable(), "CorrectPassword123")
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        Func<Task> act = () =>
            _auth.LoginAsync(new LoginRequest("admin", "WrongPassword!"));

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<UnauthorizedException>()).Which;
        ex.Message.Should().Be("Identifiants invalides.");
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ThrowsUnauthorized()
    {
        // Act
        Func<Task> act = () =>
            _auth.LoginAsync(new LoginRequest("ghost", "SomePassword"));

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<UnauthorizedException>()).Which;
        ex.Message.Should().Be("Identifiants invalides.");
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("admin", "")]
    [InlineData(null, "password")]
    [InlineData("admin", null)]
    public async Task LoginAsync_WithEmptyCredentials_ThrowsUnauthorized(string? login, string? password)
    {
        // Act
        Func<Task> act = () =>
            _auth.LoginAsync(new LoginRequest(login!, password!));

        // Assert
        await act.Should().ThrowExactlyAsync<UnauthorizedException>();
    }

    [Theory]
    [InlineData("admin' OR '1'='1")]
    [InlineData("admin' --")]
    [InlineData("admin'; DROP TABLE T_RESPONSABLE;--")]
    public async Task LoginAsync_WithSqlInjectionPayload_IsRejectedSafely(string injectionLogin)
    {
        // Arrange
        var user = new Responsable
        {
            IdResponsable = 1,
            IdRole = 1,
            Login = "admin",
            Password = _hasher.HashPassword(new Responsable(), "Secret")
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        Func<Task> act = () =>
            _auth.LoginAsync(new LoginRequest(injectionLogin, "Secret"));

        // Assert
        await act.Should().ThrowExactlyAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task LoginAsync_WithLegacyPlainTextPassword_UpgradesToHashTransparently()
    {
        // Arrange
        // Legacy account with plain text password in database
        var legacyUser = new Responsable
        {
            IdResponsable = 42,
            IdRole = 1,
            Login = "legacy_admin",
            Password = "plain_text_password"
        };

        // Act
        _uow.Get<Responsable>().Items.Add(legacyUser);

        // Assert
        PasswordHashClassifier.IsHashedPassword(legacyUser.Password).Should().BeFalse();
        // Attempt login with the legacy plain text password
        var response = await _auth.LoginAsync(new LoginRequest("legacy_admin", "plain_text_password"));
        response.Should().NotBeNull();
        response.Login.Should().Be("legacy_admin");
        // Stored password must now be upgraded to a secure PBKDF2 hash!
        PasswordHashClassifier.IsHashedPassword(legacyUser.Password).Should().BeTrue();
        legacyUser.Password.Should().NotBe("plain_text_password");
        // Next login must succeed verifying against the upgraded hash
        var nextLogin = await _auth.LoginAsync(new LoginRequest("legacy_admin", "plain_text_password"));
        nextLogin.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV2Hash_SucceedsAndTransparentlyUpgradesToV3()
    {
        // Arrange
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new Responsable
        {
            IdResponsable = 77,
            IdRole = 2,
            NomResponsable = "V2 User",
            Login = "v2user",
            Password = v2Hash
        };

        // Act
        _uow.Get<Responsable>().Items.Add(user);

        // Assert
        PasswordHashClassifier.IsIdentityV2(user.Password).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV3(user.Password).Should().BeFalse();
        // First login with V2 password
        var response = await _auth.LoginAsync(new LoginRequest("v2user", "v2_secret_password"));
        response.Should().NotBeNull();
        response.Login.Should().Be("v2user");
        // Password in DB must now be transparently upgraded to Identity V3!
        PasswordHashClassifier.IsIdentityV3(user.Password).Should().BeTrue();
        user.Password.Should().NotBe(v2Hash);
        // Subsequent login verifies via the upgraded V3 hash
        var nextLogin = await _auth.LoginAsync(new LoginRequest("v2user", "v2_secret_password"));
        nextLogin.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV2Hash_WrongPassword_ThrowsUnauthorizedAndPreservesHash()
    {
        // Arrange
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new Responsable
        {
            IdResponsable = 78,
            IdRole = 2,
            NomResponsable = "V2 User",
            Login = "v2user_wrong",
            Password = v2Hash
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        Func<Task> act = () =>
            _auth.LoginAsync(new LoginRequest("v2user_wrong", "completely_wrong_pwd"));

        // Assert
        await act.Should().ThrowExactlyAsync<UnauthorizedException>();
        // V2 hash must remain intact in DB
        user.Password.Should().Be(v2Hash);
        PasswordHashClassifier.IsIdentityV2(user.Password).Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV3Hash_SucceedsWithoutModifyingHash()
    {
        // Arrange
        var v3Hash = _hasher.HashPassword(new Responsable(), "v3_password");
        var user = new Responsable
        {
            IdResponsable = 79,
            IdRole = 1,
            NomResponsable = "V3 User",
            Login = "v3user",
            Password = v3Hash
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        var response = await _auth.LoginAsync(new LoginRequest("v3user", "v3_password"));

        // Assert
        response.Should().NotBeNull();
        // Hash was already V3, so it should not be mutated
        user.Password.Should().Be(v3Hash);
    }

    [Fact]
    public async Task LoginAsync_WithMalformedHashInDatabase_FailsSafelyWithoutCrashing()
    {
        // Arrange
        var user = new Responsable
        {
            IdResponsable = 80,
            IdRole = 2,
            NomResponsable = "Corrupt User",
            Login = "corrupt",
            Password = "NOT_A_VALID_BASE64_HASH!#@$%"
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        Func<Task> act = () =>
            _auth.LoginAsync(new LoginRequest("corrupt", "some_guess"));

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<UnauthorizedException>()).Which;
        ex.Message.Should().Be("Identifiants invalides.");
        // Value in DB was not wiped or corrupted
        user.Password.Should().Be("NOT_A_VALID_BASE64_HASH!#@$%");
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_WithMixOfPlainText_V2_V3_PreservesV2AndV3WithoutDoubleHashing()
    {
        // Arrange
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_pass");
        var v3Hash = _hasher.HashPassword(new Responsable(), "v3_pass");
        var userPlain1 = new Responsable { IdResponsable = 101, Login = "plain1", Password = "clear_password_1" };
        var userPlain2 = new Responsable { IdResponsable = 102, Login = "plain2_inactive", Password = "clear_password_2" };
        var userV2 = new Responsable { IdResponsable = 103, Login = "v2_user", Password = v2Hash };
        var userV3 = new Responsable { IdResponsable = 104, Login = "v3_user", Password = v3Hash };
        var userEmpty = new Responsable { IdResponsable = 105, Login = "empty_user", Password = "" };
        _uow.Get<Responsable>().Items.AddRange(new[] { userPlain1, userPlain2, userV2, userV3, userEmpty });

        // Act
        // First run: only userPlain1 and userPlain2 must be migrated
        var migratedCount = await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);

        // Assert
        migratedCount.Should().Be(2);
        // Plain text users must now be hashed with Identity V3
        PasswordHashClassifier.IsIdentityV3(userPlain1.Password).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV3(userPlain2.Password).Should().BeTrue();
        // CRITICAL: V2 and V3 users MUST NOT be double-hashed!
        userV2.Password.Should().Be(v2Hash);
        PasswordHashClassifier.IsIdentityV2(userV2.Password).Should().BeTrue();
        userV3.Password.Should().Be(v3Hash);
        PasswordHashClassifier.IsIdentityV3(userV3.Password).Should().BeTrue();
        // Second run: completely idempotent (0 migrated)
        var secondRun = await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);
        secondRun.Should().Be(0);
        // Verify V2 and V3 are still exactly the same
        userV2.Password.Should().Be(v2Hash);
        userV3.Password.Should().Be(v3Hash);
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_AuthenticationAfterMigration_SucceedsWithOriginalPasswords()
    {
        // Arrange
        var user = new Responsable { IdResponsable = 201, IdRole = 1, Login = "migrated_admin", Password = "InitialPassword123" };
        _uow.Get<Responsable>().Items.Add(user);
        // Run migration
        await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);

        // Act
        // Authenticate with the original password
        var authResponse = await _auth.LoginAsync(new LoginRequest("migrated_admin", "InitialPassword123"));

        // Assert
        authResponse.Should().NotBeNull();
        authResponse.Login.Should().Be("migrated_admin");
        // Authenticate with wrong password fails
        await FluentActions.Awaiting(() =>
            _auth.LoginAsync(new LoginRequest("migrated_admin", "WrongGuess"))).Should().ThrowExactlyAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_WhenCancelled_PreservesExistingData()
    {
        // Arrange
        var user = new Responsable { IdResponsable = 301, Login = "cancel_test", Password = "initial_plain" };
        _uow.Get<Responsable>().Items.Add(user);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = () =>
            UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        // Data must remain intact
        user.Password.Should().Be("initial_plain");
    }

    [Fact]
    public async Task MigrateLegacyPasswordsDetailedAsync_WithAmbiguousAndUnrecognizedHashes_PreservesValuesWithoutCorruption()
    {
        // Arrange
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_pass");
        var v3StdHash = _hasher.HashPassword(new Responsable(), "v3_pass");
        var v3Sha512Hash = PasswordHashClassifierTests.CreateIdentityV3CustomHash("sha512_pass", prf: 2, subkeySize: 64);
        var v3Sha512_32bHash = PasswordHashClassifierTests.CreateIdentityV3CustomHash("sha512_32b_pass", prf: 2, subkeySize: 32);
        const string md5Hash = "5ebe2294ecd0e0f08eab7690d2a6ee69";
        const string bcryptHash = "$2a$12$e8N4Yy9w/1z3o8hN7a5HXe8.J4vW8/x1t3y2w1z3o8hN7a5HXe8.J";
        const string corruptedV3 = "AQAAAA_corrupted_truncated";
        var userPlain = new Responsable { IdResponsable = 401, Login = "plain_user", Password = "clear_secret" };
        var userV2 = new Responsable { IdResponsable = 402, Login = "v2_user", Password = v2Hash };
        var userV3Std = new Responsable { IdResponsable = 403, Login = "v3_std", Password = v3StdHash };
        var userV3Sha512 = new Responsable { IdResponsable = 404, Login = "v3_sha512", Password = v3Sha512Hash };
        var userV3Sha512_32b = new Responsable { IdResponsable = 405, Login = "v3_sha512_32b", Password = v3Sha512_32bHash };
        var userMd5 = new Responsable { IdResponsable = 406, Login = "md5_user", Password = md5Hash };
        var userBcrypt = new Responsable { IdResponsable = 407, Login = "bcrypt_user", Password = bcryptHash };
        var userCorrupt = new Responsable { IdResponsable = 408, Login = "corrupt_user", Password = corruptedV3 };
        var userEmpty = new Responsable { IdResponsable = 409, Login = "empty_user", Password = "" };
        _uow.Get<Responsable>().Items.AddRange(new[]
        {
            userPlain, userV2, userV3Std, userV3Sha512, userV3Sha512_32b, userMd5, userBcrypt, userCorrupt, userEmpty
        });

        // Act
        // First migration run
        var report = await UserAccountConfiguration.MigrateLegacyPasswordsDetailedAsync(_uow, _hasher);

        // Assert
        report.TotalScanned.Should().Be(9);
        report.MigratedCount.Should().Be(1);
        report.AlreadyHashedV3Count.Should().Be(3);
        report.PreservedV2Count.Should().Be(1);
        report.SkippedEmptyCount.Should().Be(1);
        report.AmbiguousCount.Should().Be(3);
        report.AmbiguousAccounts.Count.Should().Be(3);
        // Plain text user must now be hashed with Identity V3
        PasswordHashClassifier.IsIdentityV3(userPlain.Password).Should().BeTrue();
        // Ambiguous, unknown and existing hashes MUST be preserved without double-hashing
        userMd5.Password.Should().Be(md5Hash);
        userBcrypt.Password.Should().Be(bcryptHash);
        userCorrupt.Password.Should().Be(corruptedV3);
        userV2.Password.Should().Be(v2Hash);
        userV3Std.Password.Should().Be(v3StdHash);
        userV3Sha512.Password.Should().Be(v3Sha512Hash);
        userV3Sha512_32b.Password.Should().Be(v3Sha512_32bHash);
        // Second run: completely idempotent
        var report2 = await UserAccountConfiguration.MigrateLegacyPasswordsDetailedAsync(_uow, _hasher);
        report2.MigratedCount.Should().Be(0);
        report2.AlreadyHashedV3Count.Should().Be(4);
        report2.AmbiguousCount.Should().Be(3);
        // Values are still intact
        userMd5.Password.Should().Be(md5Hash);
        userBcrypt.Password.Should().Be(bcryptHash);
        userCorrupt.Password.Should().Be(corruptedV3);
        userV3Sha512_32b.Password.Should().Be(v3Sha512_32bHash);
    }

    [Fact]
    public async Task LoginAsync_WithLegacyIdentityV2Hash_AuthenticatesAndUpgradesToV3()
    {
        // Arrange
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new Responsable
        {
            IdResponsable = 501,
            IdRole = 2,
            Login = "v2_login_user",
            Password = v2Hash
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        var loginResult = await _auth.LoginAsync(new LoginRequest("v2_login_user", "v2_secret_password"));

        // Assert
        loginResult.Should().NotBeNull();
        loginResult.Login.Should().Be("v2_login_user");
        // User must now be automatically upgraded to Identity V3 in storage
        PasswordHashClassifier.IsIdentityV3(user.Password).Should().BeTrue();
        user.Password.Should().NotBe(v2Hash);
    }

    [Fact]
    public async Task LoginAsync_WithIdentityV3HmacSha512_AuthenticatesSuccessfully()
    {
        // Arrange
        var hash512 = PasswordHashClassifierTests.CreateIdentityV3CustomHash("sha512_secret_pwd", prf: 2, subkeySize: 64);
        var user = new Responsable
        {
            IdResponsable = 502,
            IdRole = 2,
            Login = "sha512_user",
            Password = hash512
        };
        _uow.Get<Responsable>().Items.Add(user);

        // Act
        var loginResult = await _auth.LoginAsync(new LoginRequest("sha512_user", "sha512_secret_pwd"));

        // Assert
        loginResult.Should().NotBeNull();
        loginResult.Login.Should().Be("sha512_user");
    }

    [Fact]
    public async Task LoginAsync_WithUnrecognizedHashInDb_WhenUserEntersRawHash_FailsAuthentication()
    {
        // Arrange
        const string md5Hash = "5ebe2294ecd0e0f08eab7690d2a6ee69";
        var user = new Responsable
        {
            IdResponsable = 503,
            IdRole = 2,
            Login = "md5_locked_user",
            Password = md5Hash
        };

        // Act
        _uow.Get<Responsable>().Items.Add(user);

        // Assert
        // Attempting to log in by submitting the raw MD5 hash must fail
        await FluentActions.Awaiting(() =>
            _auth.LoginAsync(new LoginRequest("md5_locked_user", md5Hash))).Should().ThrowExactlyAsync<UnauthorizedException>();
        // Attempting with arbitrary password also fails
        await FluentActions.Awaiting(() =>
            _auth.LoginAsync(new LoginRequest("md5_locked_user", "secret"))).Should().ThrowExactlyAsync<UnauthorizedException>();
        // Value in DB is untouched
        user.Password.Should().Be(md5Hash);
    }

    [Fact]
    public async Task GetUsersPagedAsync_SecondPage_ReturnsRequestedPageOrderedByName()
    {
        // Arrange
        _uow.Get<Responsable>().Items.AddRange(new[]
        {
            new Responsable { IdResponsable = 1, IdRole = 1, NomResponsable = "Zoe", Login = "zoe" },
            new Responsable { IdResponsable = 2, IdRole = 2, NomResponsable = "Alice", Login = "alice" },
            new Responsable { IdResponsable = 3, IdRole = 2, NomResponsable = "Bob", Login = "bob" },
        });

        // Act
        var result = await _auth.GetUsersPagedAsync(1, 2);

        // Assert
        result.Total.Should().Be(3);
        result.Items.Count.Should().Be(2);
        result.Items[0].Nom.Should().Be("Alice");
        result.Items[1].Nom.Should().Be("Bob");
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetRolesPagedAsync_SecondPage_ReturnsRequestedPage()
    {
        // Act
        var result = await _auth.GetRolesPagedAsync(1, 1);

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().ContainSingle();
        result.Items[0].Nom.Should().Be("Administrateur");
        result.Page.Should().Be(1);
        result.TotalPages.Should().Be(2);
    }

    #region In-Memory Test Doubles

    #endregion
}
