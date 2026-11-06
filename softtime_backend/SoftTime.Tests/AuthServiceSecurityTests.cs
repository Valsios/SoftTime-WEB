using System.Linq.Expressions;

using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;

namespace SoftTime.Tests;

public class AuthServiceSecurityTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly FakeTokenService _tokens;
    private readonly IMapper _mapper;
    private readonly FakeEmailService _email;
    private readonly PasswordHasher<Responsable> _hasher;
    private readonly AuthServices _auth;

    public AuthServiceSecurityTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = new FakeTokenService();

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = new FakeEmailService();
        _hasher = new PasswordHasher<Responsable>();
        _auth = new AuthServices(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

        // Seed default role
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
    }

    [Fact]
    public async Task CreateAdminAsync_HashesPassword_NeverStoresPlainText()
    {
        var result = await _auth.CreateAdminAsync(new AdminSetupDto("Admin User", "admin", "AdminSecret123!", "admin@softtime.local"));

        var adminUser = _uow.GetRepo<Responsable>().Items.Single(u => u.Login == "admin");

        Assert.NotNull(adminUser.Password);
        Assert.NotEqual("AdminSecret123!", adminUser.Password);
        Assert.True(PasswordHashClassifier.IsHashedPassword(adminUser.Password));

        var verifyResult = _hasher.VerifyHashedPassword(adminUser, adminUser.Password, "AdminSecret123!");
        Assert.Equal(PasswordVerificationResult.Success, verifyResult);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_HashesPasswordAndDoesNotLeakInDto()
    {
        var userDto = new UserDto(0, 2, "Test User", "testuser", "M001", "UserPassword456!")
        {
            Email = "test@softtime.local"
        };

        var createdDto = await _auth.SaveUserAsync(userDto);

        var savedEntity = _uow.GetRepo<Responsable>().Items.Single(u => u.Login == "testuser");
        Assert.NotNull(savedEntity.Password);
        Assert.NotEqual("UserPassword456!", savedEntity.Password);
        Assert.True(PasswordHashClassifier.IsHashedPassword(savedEntity.Password));

        // Returned DTO must NEVER contain password or hash
        Assert.Null(createdDto.Password);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutPassword_ThrowsFieldValidation()
    {
        var userDto = new UserDto(0, 2, "No Pwd", "nopwd", "M002", "");

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => _auth.SaveUserAsync(userDto));

        Assert.Equal("password", ex.Field);
        Assert.Empty(_uow.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithWeakPassword_ThrowsAndDoesNotCreateUser()
    {
        var userDto = new UserDto(0, 2, "Weak User", "weakuser", "M010", "password");

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => _auth.SaveUserAsync(userDto));

        Assert.Equal("password", ex.Field);
        Assert.Contains("règles de sécurité", ex.Message);
        Assert.Empty(_uow.GetRepo<Responsable>().Items.Where(u => u.Login == "weakuser"));
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutName_ThrowsFieldValidation()
    {
        var userDto = new UserDto(0, 2, "   ", "sansnom", null, "StrongPassword123!");

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => _auth.SaveUserAsync(userDto));

        Assert.Equal("nom", ex.Field);
        Assert.Empty(_uow.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutLogin_ThrowsFieldValidation()
    {
        var userDto = new UserDto(0, 2, "Sans Login", "   ", null, "StrongPassword123!");

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => _auth.SaveUserAsync(userDto));

        Assert.Equal("login", ex.Field);
        Assert.Empty(_uow.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutRole_ThrowsFieldValidation()
    {
        var userDto = new UserDto(0, 0, "Sans Rôle", "sansrole", null, "StrongPassword123!");

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => _auth.SaveUserAsync(userDto));

        Assert.Equal("roleId", ex.Field);
        Assert.Empty(_uow.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithDuplicateLogin_ThrowsFieldValidation()
    {
        _uow.GetRepo<Responsable>().Items.Add(new Responsable
        {
            IdResponsable = 50,
            IdRole = 2,
            NomResponsable = "Existante",
            Login = "Alice",
            Password = _hasher.HashPassword(new Responsable(), "ExistingSecret123!")
        });

        var userDto = new UserDto(0, 2, "Doublon", "  alice ", null, "StrongPassword123!");

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => _auth.SaveUserAsync(userDto));

        Assert.Equal("login", ex.Field);
        Assert.Single(_uow.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingLogin_AllowsProfileUpdateEvenIfLoginIsAlreadyDuplicated()
    {
        var hash = _hasher.HashPassword(new Responsable(), "Secret123!");
        _uow.GetRepo<Responsable>().Items.AddRange(new[]
        {
            new Responsable { IdResponsable = 60, IdRole = 2, NomResponsable = "Premier", Login = "shared", Password = hash },
            new Responsable { IdResponsable = 61, IdRole = 2, NomResponsable = "Second", Login = "shared", Password = hash },
        });

        var updateDto = new UserDto(60, 2, "Premier Renommé", "shared", null, null);

        var result = await _auth.SaveUserAsync(updateDto);

        Assert.Equal("Premier Renommé", result.Nom);
        Assert.Equal(hash, _uow.GetRepo<Responsable>().Items.Single(u => u.IdResponsable == 60).Password);
    }

    [Fact]
    public async Task CreateAdminAsync_WithWeakPassword_ThrowsAndDoesNotCreateAccount()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _auth.CreateAdminAsync(new AdminSetupDto("Admin User", "admin", "weakpass", null)));

        Assert.Contains("règles de sécurité", ex.Message);
        Assert.Empty(_uow.GetRepo<Responsable>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_WithWeakNewPassword_ThrowsAndPreservesExistingHash()
    {
        var initialHash = _hasher.HashPassword(new Responsable(), "OldSecret!");
        var user = new Responsable
        {
            IdResponsable = 12,
            IdRole = 2,
            NomResponsable = "User To Update",
            Login = "weakupdate",
            Password = initialHash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var updateDto = new UserDto(12, 2, "User To Update", "weakupdate", "M011", "azerty");

        var ex = await Assert.ThrowsAsync<FieldValidationException>(() => _auth.SaveUserAsync(updateDto));

        Assert.Equal("password", ex.Field);
        var unchanged = _uow.GetRepo<Responsable>().Items.Single(u => u.IdResponsable == 12);
        Assert.Equal(initialHash, unchanged.Password);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_PreservesPasswordWhenNotProvided()
    {
        var initialHash = _hasher.HashPassword(new Responsable(), "OriginalSecret!");
        var user = new Responsable
        {
            IdResponsable = 10,
            IdRole = 2,
            NomResponsable = "Existing User",
            Login = "existing",
            Password = initialHash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        // Update profile with null / empty password
        var updateDto = new UserDto(10, 2, "Existing User Renamed", "existing", "M003", null);
        var resultDto = await _auth.SaveUserAsync(updateDto);

        var updatedEntity = _uow.GetRepo<Responsable>().Items.Single(u => u.IdResponsable == 10);
        Assert.Equal("Existing User Renamed", updatedEntity.NomResponsable);
        Assert.Equal(initialHash, updatedEntity.Password);
        Assert.Null(resultDto.Password);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_UpdatesPasswordWithNewHash()
    {
        var initialHash = _hasher.HashPassword(new Responsable(), "OldSecret!");
        var user = new Responsable
        {
            IdResponsable = 11,
            IdRole = 2,
            NomResponsable = "User To Update",
            Login = "toupdate",
            Password = initialHash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var updateDto = new UserDto(11, 2, "User To Update", "toupdate", "M004", "BrandNewSecret789!");
        var resultDto = await _auth.SaveUserAsync(updateDto);

        var updatedEntity = _uow.GetRepo<Responsable>().Items.Single(u => u.IdResponsable == 11);
        Assert.NotEqual(initialHash, updatedEntity.Password);
        Assert.True(PasswordHashClassifier.IsHashedPassword(updatedEntity.Password));

        var verify = _hasher.VerifyHashedPassword(updatedEntity, updatedEntity.Password!, "BrandNewSecret789!");
        Assert.Equal(PasswordVerificationResult.Success, verify);
        Assert.Null(resultDto.Password);
    }

    [Fact]
    public async Task LoginAsync_WithValidHashedPassword_Succeeds()
    {
        var user = new Responsable
        {
            IdResponsable = 1,
            IdRole = 1,
            NomResponsable = "Admin",
            Login = "admin",
            Password = _hasher.HashPassword(new Responsable(), "CorrectPassword123")
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var response = await _auth.LoginAsync(new LoginRequest("admin", "CorrectPassword123"));

        Assert.NotNull(response);
        Assert.Equal("admin", response.Login);
        Assert.Equal("FAKE_JWT_TOKEN_1_admin", response.Token);
    }

    [Fact]
    public async Task LoginAsync_WithIncorrectPassword_ThrowsUnauthorized()
    {
        var user = new Responsable
        {
            IdResponsable = 1,
            IdRole = 1,
            Login = "admin",
            Password = _hasher.HashPassword(new Responsable(), "CorrectPassword123")
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest("admin", "WrongPassword!")));

        Assert.Equal("Identifiants invalides.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ThrowsUnauthorized()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest("ghost", "SomePassword")));

        Assert.Equal("Identifiants invalides.", ex.Message);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("admin", "")]
    [InlineData(null, "password")]
    [InlineData("admin", null)]
    public async Task LoginAsync_WithEmptyCredentials_ThrowsUnauthorized(string? login, string? password)
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest(login!, password!)));
    }

    [Theory]
    [InlineData("admin' OR '1'='1")]
    [InlineData("admin' --")]
    [InlineData("admin'; DROP TABLE T_RESPONSABLE;--")]
    public async Task LoginAsync_WithSqlInjectionPayload_IsRejectedSafely(string injectionLogin)
    {
        var user = new Responsable
        {
            IdResponsable = 1,
            IdRole = 1,
            Login = "admin",
            Password = _hasher.HashPassword(new Responsable(), "Secret")
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest(injectionLogin, "Secret")));
    }

    [Fact]
    public async Task LoginAsync_WithLegacyPlainTextPassword_UpgradesToHashTransparently()
    {
        // Legacy account with plain text password in database
        var legacyUser = new Responsable
        {
            IdResponsable = 42,
            IdRole = 1,
            Login = "legacy_admin",
            Password = "plain_text_password"
        };
        _uow.GetRepo<Responsable>().Items.Add(legacyUser);

        Assert.False(PasswordHashClassifier.IsHashedPassword(legacyUser.Password));

        // Attempt login with the legacy plain text password
        var response = await _auth.LoginAsync(new LoginRequest("legacy_admin", "plain_text_password"));

        Assert.NotNull(response);
        Assert.Equal("legacy_admin", response.Login);

        // Stored password must now be upgraded to a secure PBKDF2 hash!
        Assert.True(PasswordHashClassifier.IsHashedPassword(legacyUser.Password));
        Assert.NotEqual("plain_text_password", legacyUser.Password);

        // Next login must succeed verifying against the upgraded hash
        var nextLogin = await _auth.LoginAsync(new LoginRequest("legacy_admin", "plain_text_password"));
        Assert.NotNull(nextLogin);
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV2Hash_SucceedsAndTransparentlyUpgradesToV3()
    {
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new Responsable
        {
            IdResponsable = 77,
            IdRole = 2,
            NomResponsable = "V2 User",
            Login = "v2user",
            Password = v2Hash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        Assert.True(PasswordHashClassifier.IsIdentityV2(user.Password));
        Assert.False(PasswordHashClassifier.IsIdentityV3(user.Password));

        // First login with V2 password
        var response = await _auth.LoginAsync(new LoginRequest("v2user", "v2_secret_password"));
        Assert.NotNull(response);
        Assert.Equal("v2user", response.Login);

        // Password in DB must now be transparently upgraded to Identity V3!
        Assert.True(PasswordHashClassifier.IsIdentityV3(user.Password));
        Assert.NotEqual(v2Hash, user.Password);

        // Subsequent login verifies via the upgraded V3 hash
        var nextLogin = await _auth.LoginAsync(new LoginRequest("v2user", "v2_secret_password"));
        Assert.NotNull(nextLogin);
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV2Hash_WrongPassword_ThrowsUnauthorizedAndPreservesHash()
    {
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new Responsable
        {
            IdResponsable = 78,
            IdRole = 2,
            NomResponsable = "V2 User",
            Login = "v2user_wrong",
            Password = v2Hash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest("v2user_wrong", "completely_wrong_pwd")));

        // V2 hash must remain intact in DB
        Assert.Equal(v2Hash, user.Password);
        Assert.True(PasswordHashClassifier.IsIdentityV2(user.Password));
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV3Hash_SucceedsWithoutModifyingHash()
    {
        var v3Hash = _hasher.HashPassword(new Responsable(), "v3_password");
        var user = new Responsable
        {
            IdResponsable = 79,
            IdRole = 1,
            NomResponsable = "V3 User",
            Login = "v3user",
            Password = v3Hash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var response = await _auth.LoginAsync(new LoginRequest("v3user", "v3_password"));
        Assert.NotNull(response);

        // Hash was already V3, so it should not be mutated
        Assert.Equal(v3Hash, user.Password);
    }

    [Fact]
    public async Task LoginAsync_WithMalformedHashInDatabase_FailsSafelyWithoutCrashing()
    {
        var user = new Responsable
        {
            IdResponsable = 80,
            IdRole = 2,
            NomResponsable = "Corrupt User",
            Login = "corrupt",
            Password = "NOT_A_VALID_BASE64_HASH!#@$%"
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest("corrupt", "some_guess")));

        Assert.Equal("Identifiants invalides.", ex.Message);
        // Value in DB was not wiped or corrupted
        Assert.Equal("NOT_A_VALID_BASE64_HASH!#@$%", user.Password);
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_WithMixOfPlainText_V2_V3_PreservesV2AndV3WithoutDoubleHashing()
    {
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_pass");
        var v3Hash = _hasher.HashPassword(new Responsable(), "v3_pass");

        var userPlain1 = new Responsable { IdResponsable = 101, Login = "plain1", Password = "clear_password_1" };
        var userPlain2 = new Responsable { IdResponsable = 102, Login = "plain2_inactive", Password = "clear_password_2" };
        var userV2 = new Responsable { IdResponsable = 103, Login = "v2_user", Password = v2Hash };
        var userV3 = new Responsable { IdResponsable = 104, Login = "v3_user", Password = v3Hash };
        var userEmpty = new Responsable { IdResponsable = 105, Login = "empty_user", Password = "" };

        _uow.GetRepo<Responsable>().Items.AddRange(new[] { userPlain1, userPlain2, userV2, userV3, userEmpty });

        // First run: only userPlain1 and userPlain2 must be migrated
        var migratedCount = await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);
        Assert.Equal(2, migratedCount);

        // Plain text users must now be hashed with Identity V3
        Assert.True(PasswordHashClassifier.IsIdentityV3(userPlain1.Password));
        Assert.True(PasswordHashClassifier.IsIdentityV3(userPlain2.Password));

        // CRITICAL: V2 and V3 users MUST NOT be double-hashed!
        Assert.Equal(v2Hash, userV2.Password);
        Assert.True(PasswordHashClassifier.IsIdentityV2(userV2.Password));

        Assert.Equal(v3Hash, userV3.Password);
        Assert.True(PasswordHashClassifier.IsIdentityV3(userV3.Password));

        // Second run: completely idempotent (0 migrated)
        var secondRun = await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);
        Assert.Equal(0, secondRun);

        // Verify V2 and V3 are still exactly the same
        Assert.Equal(v2Hash, userV2.Password);
        Assert.Equal(v3Hash, userV3.Password);
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_AuthenticationAfterMigration_SucceedsWithOriginalPasswords()
    {
        var user = new Responsable { IdResponsable = 201, IdRole = 1, Login = "migrated_admin", Password = "InitialPassword123" };
        _uow.GetRepo<Responsable>().Items.Add(user);

        // Run migration
        await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);

        // Authenticate with the original password
        var authResponse = await _auth.LoginAsync(new LoginRequest("migrated_admin", "InitialPassword123"));
        Assert.NotNull(authResponse);
        Assert.Equal("migrated_admin", authResponse.Login);

        // Authenticate with wrong password fails
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest("migrated_admin", "WrongGuess")));
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_WhenCancelled_PreservesExistingData()
    {
        var user = new Responsable { IdResponsable = 301, Login = "cancel_test", Password = "initial_plain" };
        _uow.GetRepo<Responsable>().Items.Add(user);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancelled before execution

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher, cts.Token));

        // Data must remain intact
        Assert.Equal("initial_plain", user.Password);
    }

    [Fact]
    public async Task MigrateLegacyPasswordsDetailedAsync_WithAmbiguousAndUnrecognizedHashes_PreservesValuesWithoutCorruption()
    {
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

        _uow.GetRepo<Responsable>().Items.AddRange(new[]
        {
            userPlain, userV2, userV3Std, userV3Sha512, userV3Sha512_32b, userMd5, userBcrypt, userCorrupt, userEmpty
        });

        // First migration run
        var report = await UserAccountConfiguration.MigrateLegacyPasswordsDetailedAsync(_uow, _hasher);

        Assert.Equal(9, report.TotalScanned);
        Assert.Equal(1, report.MigratedCount); // Only userPlain
        Assert.Equal(3, report.AlreadyHashedV3Count); // userV3Std, userV3Sha512, and userV3Sha512_32b
        Assert.Equal(1, report.PreservedV2Count); // userV2
        Assert.Equal(1, report.SkippedEmptyCount); // userEmpty
        Assert.Equal(3, report.AmbiguousCount); // userMd5, userBcrypt, userCorrupt
        Assert.Equal(3, report.AmbiguousAccounts.Count);

        // Plain text user must now be hashed with Identity V3
        Assert.True(PasswordHashClassifier.IsIdentityV3(userPlain.Password));

        // Ambiguous, unknown and existing hashes MUST be preserved without double-hashing
        Assert.Equal(md5Hash, userMd5.Password);
        Assert.Equal(bcryptHash, userBcrypt.Password);
        Assert.Equal(corruptedV3, userCorrupt.Password);
        Assert.Equal(v2Hash, userV2.Password);
        Assert.Equal(v3StdHash, userV3Std.Password);
        Assert.Equal(v3Sha512Hash, userV3Sha512.Password);
        Assert.Equal(v3Sha512_32bHash, userV3Sha512_32b.Password);

        // Second run: completely idempotent
        var report2 = await UserAccountConfiguration.MigrateLegacyPasswordsDetailedAsync(_uow, _hasher);
        Assert.Equal(0, report2.MigratedCount);
        Assert.Equal(4, report2.AlreadyHashedV3Count); // userPlain is now V3 too
        Assert.Equal(3, report2.AmbiguousCount);

        // Values are still intact
        Assert.Equal(md5Hash, userMd5.Password);
        Assert.Equal(bcryptHash, userBcrypt.Password);
        Assert.Equal(corruptedV3, userCorrupt.Password);
        Assert.Equal(v3Sha512_32bHash, userV3Sha512_32b.Password);
    }

    [Fact]
    public async Task LoginAsync_WithLegacyIdentityV2Hash_AuthenticatesAndUpgradesToV3()
    {
        var v2Hash = PasswordHashClassifierTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new Responsable
        {
            IdResponsable = 501,
            IdRole = 2,
            Login = "v2_login_user",
            Password = v2Hash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var loginResult = await _auth.LoginAsync(new LoginRequest("v2_login_user", "v2_secret_password"));
        Assert.NotNull(loginResult);
        Assert.Equal("v2_login_user", loginResult.Login);

        // User must now be automatically upgraded to Identity V3 in storage
        Assert.True(PasswordHashClassifier.IsIdentityV3(user.Password));
        Assert.NotEqual(v2Hash, user.Password);
    }

    [Fact]
    public async Task LoginAsync_WithIdentityV3HmacSha512_AuthenticatesSuccessfully()
    {
        var hash512 = PasswordHashClassifierTests.CreateIdentityV3CustomHash("sha512_secret_pwd", prf: 2, subkeySize: 64);
        var user = new Responsable
        {
            IdResponsable = 502,
            IdRole = 2,
            Login = "sha512_user",
            Password = hash512
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        var loginResult = await _auth.LoginAsync(new LoginRequest("sha512_user", "sha512_secret_pwd"));
        Assert.NotNull(loginResult);
        Assert.Equal("sha512_user", loginResult.Login);
    }

    [Fact]
    public async Task LoginAsync_WithUnrecognizedHashInDb_WhenUserEntersRawHash_FailsAuthentication()
    {
        const string md5Hash = "5ebe2294ecd0e0f08eab7690d2a6ee69";
        var user = new Responsable
        {
            IdResponsable = 503,
            IdRole = 2,
            Login = "md5_locked_user",
            Password = md5Hash
        };
        _uow.GetRepo<Responsable>().Items.Add(user);

        // Attempting to log in by submitting the raw MD5 hash must fail
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest("md5_locked_user", md5Hash)));

        // Attempting with arbitrary password also fails
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest("md5_locked_user", "secret")));

        // Value in DB is untouched
        Assert.Equal(md5Hash, user.Password);
    }

    [Fact]
    public async Task GetUsersPagedAsync_ReturnsRequestedPageOrderedByName()
    {
        _uow.GetRepo<Responsable>().Items.AddRange(new[]
        {
            new Responsable { IdResponsable = 1, IdRole = 1, NomResponsable = "Zoe", Login = "zoe" },
            new Responsable { IdResponsable = 2, IdRole = 2, NomResponsable = "Alice", Login = "alice" },
            new Responsable { IdResponsable = 3, IdRole = 2, NomResponsable = "Bob", Login = "bob" },
        });

        var result = await _auth.GetUsersPagedAsync(1, 2);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Alice", result.Items[0].Nom);
        Assert.Equal("Bob", result.Items[1].Nom);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task GetRolesPagedAsync_ReturnsRequestedPage()
    {
        var result = await _auth.GetRolesPagedAsync(1, 1);

        Assert.Equal(2, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("Administrateur", result.Items[0].Nom);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.TotalPages);
    }

    #region In-Memory Test Doubles

    private sealed class InMemoryRepository<T> : IRepository<T> where T : class
    {
        public List<T> Items { get; } = new();

        public IQueryable<T> Query() => Items.AsQueryable();

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            var decId = Convert.ToDecimal(id);
            var match = Items.FirstOrDefault(i =>
            {
                if (i is Responsable r && r.IdResponsable == decId) return true;
                if (i is Role ro && ro.IdRole == decId) return true;
                return false;
            });
            return Task.FromResult(match);
        }

        public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            return Task.FromResult(Items.Where(func).ToList());
        }

        public Task<List<T>> ListLatestAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<T, DateTime?>> orderByDescending,
            int take,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            var orderFunc = orderByDescending.Compile();
            return Task.FromResult(Items.Where(func).OrderByDescending(orderFunc).Take(take).ToList());
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is Responsable r && r.IdResponsable == 0)
            {
                r.IdResponsable = Items.Count == 0 ? 1 : Items.OfType<Responsable>().Max(x => x.IdResponsable) + 1;
            }
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            foreach (var e in entities)
                AddAsync(e, cancellationToken);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Remove(T entity) => Items.Remove(entity);
        public void RemoveRange(IEnumerable<T> entities)
        {
            foreach (var e in entities.ToList())
                Items.Remove(e);
        }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repos = new();

        public InMemoryRepository<T> GetRepo<T>() where T : class
        {
            if (!_repos.TryGetValue(typeof(T), out var r))
            {
                r = new InMemoryRepository<T>();
                _repos[typeof(T)] = r;
            }
            return (InMemoryRepository<T>)r;
        }

        public IRepository<T> Repository<T>() where T : class => GetRepo<T>();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights)
            => $"FAKE_JWT_TOKEN_{userId}_{login}";
    }

    private sealed class FakeEmailService : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    #endregion
}
