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
    private readonly PasswordHasher<T_RESPONSABLE> _hasher;
    private readonly AuthService _auth;

    public AuthServiceSecurityTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = new FakeTokenService();

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = new FakeEmailService();
        _hasher = new PasswordHasher<T_RESPONSABLE>();
        _auth = new AuthService(_uow, _tokens, _mapper, _email, _hasher);

        // Seed default role
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Utilisateur" });
    }

    [Fact]
    public async Task CreateAdminAsync_HashesPassword_NeverStoresPlainText()
    {
        var result = await _auth.CreateAdminAsync(new AdminSetupDto("Admin User", "admin", "AdminSecret123!", "admin@softtime.local"));

        var adminUser = _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.LOGIN == "admin");

        Assert.NotNull(adminUser.PASSWORD);
        Assert.NotEqual("AdminSecret123!", adminUser.PASSWORD);
        Assert.True(PasswordSecurityHelper.IsHashedPassword(adminUser.PASSWORD));

        var verifyResult = _hasher.VerifyHashedPassword(adminUser, adminUser.PASSWORD, "AdminSecret123!");
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

        var savedEntity = _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.LOGIN == "testuser");
        Assert.NotNull(savedEntity.PASSWORD);
        Assert.NotEqual("UserPassword456!", savedEntity.PASSWORD);
        Assert.True(PasswordSecurityHelper.IsHashedPassword(savedEntity.PASSWORD));

        // Returned DTO must NEVER contain password or hash
        Assert.Null(createdDto.Password);
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithoutPassword_ThrowsException()
    {
        var userDto = new UserDto(0, 2, "No Pwd", "nopwd", "M002", "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(userDto));
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_WithWeakPassword_ThrowsAndDoesNotCreateUser()
    {
        var userDto = new UserDto(0, 2, "Weak User", "weakuser", "M010", "password");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(userDto));

        Assert.Contains("règles de sécurité", ex.Message);
        Assert.Empty(_uow.GetRepo<T_RESPONSABLE>().Items.Where(u => u.LOGIN == "weakuser"));
    }

    [Fact]
    public async Task CreateAdminAsync_WithWeakPassword_ThrowsAndDoesNotCreateAccount()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.CreateAdminAsync(new AdminSetupDto("Admin User", "admin", "weakpass", null)));

        Assert.Contains("règles de sécurité", ex.Message);
        Assert.Empty(_uow.GetRepo<T_RESPONSABLE>().Items);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_WithWeakNewPassword_ThrowsAndPreservesExistingHash()
    {
        var initialHash = _hasher.HashPassword(new T_RESPONSABLE(), "OldSecret!");
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 12,
            IDROLE = 2,
            NOMRESPONSABLE = "User To Update",
            LOGIN = "weakupdate",
            PASSWORD = initialHash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var updateDto = new UserDto(12, 2, "User To Update", "weakupdate", "M011", "azerty");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(updateDto));

        var unchanged = _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 12);
        Assert.Equal(initialHash, unchanged.PASSWORD);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_PreservesPasswordWhenNotProvided()
    {
        var initialHash = _hasher.HashPassword(new T_RESPONSABLE(), "OriginalSecret!");
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 10,
            IDROLE = 2,
            NOMRESPONSABLE = "Existing User",
            LOGIN = "existing",
            PASSWORD = initialHash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        // Update profile with null / empty password
        var updateDto = new UserDto(10, 2, "Existing User Renamed", "existing", "M003", null);
        var resultDto = await _auth.SaveUserAsync(updateDto);

        var updatedEntity = _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 10);
        Assert.Equal("Existing User Renamed", updatedEntity.NOMRESPONSABLE);
        Assert.Equal(initialHash, updatedEntity.PASSWORD);
        Assert.Null(resultDto.Password);
    }

    [Fact]
    public async Task SaveUserAsync_ExistingUser_UpdatesPasswordWithNewHash()
    {
        var initialHash = _hasher.HashPassword(new T_RESPONSABLE(), "OldSecret!");
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 11,
            IDROLE = 2,
            NOMRESPONSABLE = "User To Update",
            LOGIN = "toupdate",
            PASSWORD = initialHash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var updateDto = new UserDto(11, 2, "User To Update", "toupdate", "M004", "BrandNewSecret789!");
        var resultDto = await _auth.SaveUserAsync(updateDto);

        var updatedEntity = _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 11);
        Assert.NotEqual(initialHash, updatedEntity.PASSWORD);
        Assert.True(PasswordSecurityHelper.IsHashedPassword(updatedEntity.PASSWORD));

        var verify = _hasher.VerifyHashedPassword(updatedEntity, updatedEntity.PASSWORD!, "BrandNewSecret789!");
        Assert.Equal(PasswordVerificationResult.Success, verify);
        Assert.Null(resultDto.Password);
    }

    [Fact]
    public async Task LoginAsync_WithValidHashedPassword_Succeeds()
    {
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 1,
            IDROLE = 1,
            NOMRESPONSABLE = "Admin",
            LOGIN = "admin",
            PASSWORD = _hasher.HashPassword(new T_RESPONSABLE(), "CorrectPassword123")
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var response = await _auth.LoginAsync(new LoginRequest("admin", "CorrectPassword123"));

        Assert.NotNull(response);
        Assert.Equal("admin", response.Login);
        Assert.Equal("FAKE_JWT_TOKEN_1_admin", response.Token);
    }

    [Fact]
    public async Task LoginAsync_WithIncorrectPassword_ThrowsUnauthorized()
    {
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 1,
            IDROLE = 1,
            LOGIN = "admin",
            PASSWORD = _hasher.HashPassword(new T_RESPONSABLE(), "CorrectPassword123")
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest("admin", "WrongPassword!")));

        Assert.Equal("Identifiants invalides.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ThrowsUnauthorized()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest(login!, password!)));
    }

    [Theory]
    [InlineData("admin' OR '1'='1")]
    [InlineData("admin' --")]
    [InlineData("admin'; DROP TABLE T_RESPONSABLE;--")]
    public async Task LoginAsync_WithSqlInjectionPayload_IsRejectedSafely(string injectionLogin)
    {
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 1,
            IDROLE = 1,
            LOGIN = "admin",
            PASSWORD = _hasher.HashPassword(new T_RESPONSABLE(), "Secret")
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest(injectionLogin, "Secret")));
    }

    [Fact]
    public async Task LoginAsync_WithLegacyPlainTextPassword_UpgradesToHashTransparently()
    {
        // Legacy account with plain text password in database
        var legacyUser = new T_RESPONSABLE
        {
            IDRESPONSABLE = 42,
            IDROLE = 1,
            LOGIN = "legacy_admin",
            PASSWORD = "plain_text_password"
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(legacyUser);

        Assert.False(PasswordSecurityHelper.IsHashedPassword(legacyUser.PASSWORD));

        // Attempt login with the legacy plain text password
        var response = await _auth.LoginAsync(new LoginRequest("legacy_admin", "plain_text_password"));

        Assert.NotNull(response);
        Assert.Equal("legacy_admin", response.Login);

        // Stored password must now be upgraded to a secure PBKDF2 hash!
        Assert.True(PasswordSecurityHelper.IsHashedPassword(legacyUser.PASSWORD));
        Assert.NotEqual("plain_text_password", legacyUser.PASSWORD);

        // Next login must succeed verifying against the upgraded hash
        var nextLogin = await _auth.LoginAsync(new LoginRequest("legacy_admin", "plain_text_password"));
        Assert.NotNull(nextLogin);
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV2Hash_SucceedsAndTransparentlyUpgradesToV3()
    {
        var v2Hash = PasswordSecurityHelperTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 77,
            IDROLE = 2,
            NOMRESPONSABLE = "V2 User",
            LOGIN = "v2user",
            PASSWORD = v2Hash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        Assert.True(PasswordSecurityHelper.IsIdentityV2(user.PASSWORD));
        Assert.False(PasswordSecurityHelper.IsIdentityV3(user.PASSWORD));

        // First login with V2 password
        var response = await _auth.LoginAsync(new LoginRequest("v2user", "v2_secret_password"));
        Assert.NotNull(response);
        Assert.Equal("v2user", response.Login);

        // Password in DB must now be transparently upgraded to Identity V3!
        Assert.True(PasswordSecurityHelper.IsIdentityV3(user.PASSWORD));
        Assert.NotEqual(v2Hash, user.PASSWORD);

        // Subsequent login verifies via the upgraded V3 hash
        var nextLogin = await _auth.LoginAsync(new LoginRequest("v2user", "v2_secret_password"));
        Assert.NotNull(nextLogin);
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV2Hash_WrongPassword_ThrowsUnauthorizedAndPreservesHash()
    {
        var v2Hash = PasswordSecurityHelperTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 78,
            IDROLE = 2,
            NOMRESPONSABLE = "V2 User",
            LOGIN = "v2user_wrong",
            PASSWORD = v2Hash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest("v2user_wrong", "completely_wrong_pwd")));

        // V2 hash must remain intact in DB
        Assert.Equal(v2Hash, user.PASSWORD);
        Assert.True(PasswordSecurityHelper.IsIdentityV2(user.PASSWORD));
    }

    [Fact]
    public async Task LoginAsync_WithValidIdentityV3Hash_SucceedsWithoutModifyingHash()
    {
        var v3Hash = _hasher.HashPassword(new T_RESPONSABLE(), "v3_password");
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 79,
            IDROLE = 1,
            NOMRESPONSABLE = "V3 User",
            LOGIN = "v3user",
            PASSWORD = v3Hash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var response = await _auth.LoginAsync(new LoginRequest("v3user", "v3_password"));
        Assert.NotNull(response);

        // Hash was already V3, so it should not be mutated
        Assert.Equal(v3Hash, user.PASSWORD);
    }

    [Fact]
    public async Task LoginAsync_WithMalformedHashInDatabase_FailsSafelyWithoutCrashing()
    {
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 80,
            IDROLE = 2,
            NOMRESPONSABLE = "Corrupt User",
            LOGIN = "corrupt",
            PASSWORD = "NOT_A_VALID_BASE64_HASH!#@$%"
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest("corrupt", "some_guess")));

        Assert.Equal("Identifiants invalides.", ex.Message);
        // Value in DB was not wiped or corrupted
        Assert.Equal("NOT_A_VALID_BASE64_HASH!#@$%", user.PASSWORD);
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_WithMixOfPlainText_V2_V3_PreservesV2AndV3WithoutDoubleHashing()
    {
        var v2Hash = PasswordSecurityHelperTests.CreateLegacyIdentityV2Hash("v2_pass");
        var v3Hash = _hasher.HashPassword(new T_RESPONSABLE(), "v3_pass");

        var userPlain1 = new T_RESPONSABLE { IDRESPONSABLE = 101, LOGIN = "plain1", PASSWORD = "clear_password_1" };
        var userPlain2 = new T_RESPONSABLE { IDRESPONSABLE = 102, LOGIN = "plain2_inactive", PASSWORD = "clear_password_2" };
        var userV2 = new T_RESPONSABLE { IDRESPONSABLE = 103, LOGIN = "v2_user", PASSWORD = v2Hash };
        var userV3 = new T_RESPONSABLE { IDRESPONSABLE = 104, LOGIN = "v3_user", PASSWORD = v3Hash };
        var userEmpty = new T_RESPONSABLE { IDRESPONSABLE = 105, LOGIN = "empty_user", PASSWORD = "" };

        _uow.GetRepo<T_RESPONSABLE>().Items.AddRange(new[] { userPlain1, userPlain2, userV2, userV3, userEmpty });

        // First run: only userPlain1 and userPlain2 must be migrated
        var migratedCount = await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);
        Assert.Equal(2, migratedCount);

        // Plain text users must now be hashed with Identity V3
        Assert.True(PasswordSecurityHelper.IsIdentityV3(userPlain1.PASSWORD));
        Assert.True(PasswordSecurityHelper.IsIdentityV3(userPlain2.PASSWORD));

        // CRITICAL: V2 and V3 users MUST NOT be double-hashed!
        Assert.Equal(v2Hash, userV2.PASSWORD);
        Assert.True(PasswordSecurityHelper.IsIdentityV2(userV2.PASSWORD));

        Assert.Equal(v3Hash, userV3.PASSWORD);
        Assert.True(PasswordSecurityHelper.IsIdentityV3(userV3.PASSWORD));

        // Second run: completely idempotent (0 migrated)
        var secondRun = await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);
        Assert.Equal(0, secondRun);

        // Verify V2 and V3 are still exactly the same
        Assert.Equal(v2Hash, userV2.PASSWORD);
        Assert.Equal(v3Hash, userV3.PASSWORD);
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_AuthenticationAfterMigration_SucceedsWithOriginalPasswords()
    {
        var user = new T_RESPONSABLE { IDRESPONSABLE = 201, IDROLE = 1, LOGIN = "migrated_admin", PASSWORD = "InitialPassword123" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        // Run migration
        await UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher);

        // Authenticate with the original password
        var authResponse = await _auth.LoginAsync(new LoginRequest("migrated_admin", "InitialPassword123"));
        Assert.NotNull(authResponse);
        Assert.Equal("migrated_admin", authResponse.Login);

        // Authenticate with wrong password fails
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest("migrated_admin", "WrongGuess")));
    }

    [Fact]
    public async Task MigrateLegacyPasswordsAsync_WhenCancelled_PreservesExistingData()
    {
        var user = new T_RESPONSABLE { IDRESPONSABLE = 301, LOGIN = "cancel_test", PASSWORD = "initial_plain" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancelled before execution

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UserAccountConfiguration.MigrateLegacyPasswordsAsync(_uow, _hasher, cts.Token));

        // Data must remain intact
        Assert.Equal("initial_plain", user.PASSWORD);
    }

    [Fact]
    public async Task MigrateLegacyPasswordsDetailedAsync_WithAmbiguousAndUnrecognizedHashes_PreservesValuesWithoutCorruption()
    {
        var v2Hash = PasswordSecurityHelperTests.CreateLegacyIdentityV2Hash("v2_pass");
        var v3StdHash = _hasher.HashPassword(new T_RESPONSABLE(), "v3_pass");
        var v3Sha512Hash = PasswordSecurityHelperTests.CreateIdentityV3CustomHash("sha512_pass", prf: 2, subkeySize: 64);
        var v3Sha512_32bHash = PasswordSecurityHelperTests.CreateIdentityV3CustomHash("sha512_32b_pass", prf: 2, subkeySize: 32);
        const string md5Hash = "5ebe2294ecd0e0f08eab7690d2a6ee69";
        const string bcryptHash = "$2a$12$e8N4Yy9w/1z3o8hN7a5HXe8.J4vW8/x1t3y2w1z3o8hN7a5HXe8.J";
        const string corruptedV3 = "AQAAAA_corrupted_truncated";

        var userPlain = new T_RESPONSABLE { IDRESPONSABLE = 401, LOGIN = "plain_user", PASSWORD = "clear_secret" };
        var userV2 = new T_RESPONSABLE { IDRESPONSABLE = 402, LOGIN = "v2_user", PASSWORD = v2Hash };
        var userV3Std = new T_RESPONSABLE { IDRESPONSABLE = 403, LOGIN = "v3_std", PASSWORD = v3StdHash };
        var userV3Sha512 = new T_RESPONSABLE { IDRESPONSABLE = 404, LOGIN = "v3_sha512", PASSWORD = v3Sha512Hash };
        var userV3Sha512_32b = new T_RESPONSABLE { IDRESPONSABLE = 405, LOGIN = "v3_sha512_32b", PASSWORD = v3Sha512_32bHash };
        var userMd5 = new T_RESPONSABLE { IDRESPONSABLE = 406, LOGIN = "md5_user", PASSWORD = md5Hash };
        var userBcrypt = new T_RESPONSABLE { IDRESPONSABLE = 407, LOGIN = "bcrypt_user", PASSWORD = bcryptHash };
        var userCorrupt = new T_RESPONSABLE { IDRESPONSABLE = 408, LOGIN = "corrupt_user", PASSWORD = corruptedV3 };
        var userEmpty = new T_RESPONSABLE { IDRESPONSABLE = 409, LOGIN = "empty_user", PASSWORD = "" };

        _uow.GetRepo<T_RESPONSABLE>().Items.AddRange(new[]
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
        Assert.True(PasswordSecurityHelper.IsIdentityV3(userPlain.PASSWORD));

        // Ambiguous, unknown and existing hashes MUST be preserved without double-hashing
        Assert.Equal(md5Hash, userMd5.PASSWORD);
        Assert.Equal(bcryptHash, userBcrypt.PASSWORD);
        Assert.Equal(corruptedV3, userCorrupt.PASSWORD);
        Assert.Equal(v2Hash, userV2.PASSWORD);
        Assert.Equal(v3StdHash, userV3Std.PASSWORD);
        Assert.Equal(v3Sha512Hash, userV3Sha512.PASSWORD);
        Assert.Equal(v3Sha512_32bHash, userV3Sha512_32b.PASSWORD);

        // Second run: completely idempotent
        var report2 = await UserAccountConfiguration.MigrateLegacyPasswordsDetailedAsync(_uow, _hasher);
        Assert.Equal(0, report2.MigratedCount);
        Assert.Equal(4, report2.AlreadyHashedV3Count); // userPlain is now V3 too
        Assert.Equal(3, report2.AmbiguousCount);

        // Values are still intact
        Assert.Equal(md5Hash, userMd5.PASSWORD);
        Assert.Equal(bcryptHash, userBcrypt.PASSWORD);
        Assert.Equal(corruptedV3, userCorrupt.PASSWORD);
        Assert.Equal(v3Sha512_32bHash, userV3Sha512_32b.PASSWORD);
    }

    [Fact]
    public async Task LoginAsync_WithLegacyIdentityV2Hash_AuthenticatesAndUpgradesToV3()
    {
        var v2Hash = PasswordSecurityHelperTests.CreateLegacyIdentityV2Hash("v2_secret_password");
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 501,
            IDROLE = 2,
            LOGIN = "v2_login_user",
            PASSWORD = v2Hash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var loginResult = await _auth.LoginAsync(new LoginRequest("v2_login_user", "v2_secret_password"));
        Assert.NotNull(loginResult);
        Assert.Equal("v2_login_user", loginResult.Login);

        // User must now be automatically upgraded to Identity V3 in storage
        Assert.True(PasswordSecurityHelper.IsIdentityV3(user.PASSWORD));
        Assert.NotEqual(v2Hash, user.PASSWORD);
    }

    [Fact]
    public async Task LoginAsync_WithIdentityV3HmacSha512_AuthenticatesSuccessfully()
    {
        var hash512 = PasswordSecurityHelperTests.CreateIdentityV3CustomHash("sha512_secret_pwd", prf: 2, subkeySize: 64);
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 502,
            IDROLE = 2,
            LOGIN = "sha512_user",
            PASSWORD = hash512
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var loginResult = await _auth.LoginAsync(new LoginRequest("sha512_user", "sha512_secret_pwd"));
        Assert.NotNull(loginResult);
        Assert.Equal("sha512_user", loginResult.Login);
    }

    [Fact]
    public async Task LoginAsync_WithUnrecognizedHashInDb_WhenUserEntersRawHash_FailsAuthentication()
    {
        const string md5Hash = "5ebe2294ecd0e0f08eab7690d2a6ee69";
        var user = new T_RESPONSABLE
        {
            IDRESPONSABLE = 503,
            IDROLE = 2,
            LOGIN = "md5_locked_user",
            PASSWORD = md5Hash
        };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        // Attempting to log in by submitting the raw MD5 hash must fail
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest("md5_locked_user", md5Hash)));

        // Attempting with arbitrary password also fails
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _auth.LoginAsync(new LoginRequest("md5_locked_user", "secret")));

        // Value in DB is untouched
        Assert.Equal(md5Hash, user.PASSWORD);
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
                if (i is T_RESPONSABLE r && r.IDRESPONSABLE == decId) return true;
                if (i is T_ROLE ro && ro.IDROLE == decId) return true;
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

        public Task<List<T>> ListPagedAsync(
            Expression<Func<T, bool>> predicate,
            Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            var items = orderBy(Items.Where(func).AsQueryable()).Skip(skip).Take(take).ToList();
            return Task.FromResult(items);
        }

        public Task<int> CountAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            return Task.FromResult(Items.Count(func));
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is T_RESPONSABLE r && r.IDRESPONSABLE == 0)
            {
                r.IDRESPONSABLE = Items.Count == 0 ? 1 : Items.OfType<T_RESPONSABLE>().Max(x => x.IDRESPONSABLE) + 1;
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
