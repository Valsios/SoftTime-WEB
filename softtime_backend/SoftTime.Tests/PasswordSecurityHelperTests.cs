using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class PasswordSecurityHelperTests
{
    private readonly PasswordHasher<T_RESPONSABLE> _hasher = new();

    public static string CreateLegacyIdentityV2Hash(string password)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: 1000,
            hashAlgorithm: HashAlgorithmName.SHA1,
            outputLength: 32);

        var outputBytes = new byte[49];
        outputBytes[0] = 0x00; // Identity V2 marker
        Buffer.BlockCopy(salt, 0, outputBytes, 1, 16);
        Buffer.BlockCopy(subkey, 0, outputBytes, 17, 32);
        return Convert.ToBase64String(outputBytes);
    }

    public static string CreateIdentityV3CustomHash(
        string password,
        uint prf = 2, // 0 = HMACSHA1, 1 = HMACSHA256, 2 = HMACSHA512
        int iterationCount = 100000,
        int saltSize = 16,
        int subkeySize = 64)
    {
        var hashAlgo = prf switch
        {
            0 => HashAlgorithmName.SHA1,
            1 => HashAlgorithmName.SHA256,
            2 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(prf))
        };

        var salt = new byte[saltSize];
        RandomNumberGenerator.Fill(salt);

        byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: iterationCount,
            hashAlgorithm: hashAlgo,
            outputLength: subkeySize);

        var outputBytes = new byte[13 + salt.Length + subkey.Length];
        outputBytes[0] = 0x01; // format marker V3
        WriteNetworkByteOrder(outputBytes, 1, prf);
        WriteNetworkByteOrder(outputBytes, 5, (uint)iterationCount);
        WriteNetworkByteOrder(outputBytes, 9, (uint)saltSize);
        Buffer.BlockCopy(salt, 0, outputBytes, 13, salt.Length);
        Buffer.BlockCopy(subkey, 0, outputBytes, 13 + saltSize, subkey.Length);

        return Convert.ToBase64String(outputBytes);
    }

    private static void WriteNetworkByteOrder(byte[] buffer, int offset, uint value)
    {
        buffer[offset + 0] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)(value >> 0);
    }

    [Fact]
    public void IsHashedPassword_WithNullOrEmptyOrWhitespace_ReturnsFalse()
    {
        Assert.False(PasswordSecurityHelper.IsHashedPassword(null));
        Assert.False(PasswordSecurityHelper.IsHashedPassword(""));
        Assert.False(PasswordSecurityHelper.IsHashedPassword("   "));
        Assert.Equal(PasswordHashVersion.None, PasswordSecurityHelper.GetHashVersion(null));
        Assert.Equal(PasswordHashVersion.None, PasswordSecurityHelper.GetHashVersion(""));
        Assert.Equal(PasswordHashVersion.None, PasswordSecurityHelper.GetHashVersion("   "));
        Assert.Equal(PasswordClassification.NullOrEmpty, PasswordSecurityHelper.Classify(null));
        Assert.Equal(PasswordClassification.NullOrEmpty, PasswordSecurityHelper.Classify(""));
        Assert.Equal(PasswordClassification.NullOrEmpty, PasswordSecurityHelper.Classify("   "));
        Assert.False(PasswordSecurityHelper.IsPlainText(null));
        Assert.False(PasswordSecurityHelper.IsPlainText(""));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("password123")]
    [InlineData("SuperSecret!2026")]
    [InlineData("azerty")]
    [InlineData("123456")]
    public void IsHashedPassword_WithPlainTextPasswords_ReturnsFalse(string plainText)
    {
        Assert.False(PasswordSecurityHelper.IsHashedPassword(plainText));
        Assert.False(PasswordSecurityHelper.IsIdentityV2(plainText));
        Assert.False(PasswordSecurityHelper.IsIdentityV3(plainText));
        Assert.Equal(PasswordHashVersion.None, PasswordSecurityHelper.GetHashVersion(plainText));
        Assert.Equal(PasswordClassification.LegacyPlainText, PasswordSecurityHelper.Classify(plainText));
        Assert.True(PasswordSecurityHelper.IsPlainText(plainText));
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV3StandardHash_ReturnsTrueAndIdentifiesV3()
    {
        var user = new T_RESPONSABLE { IDRESPONSABLE = 1, LOGIN = "admin" };
        var hash = _hasher.HashPassword(user, "admin");

        Assert.NotNull(hash);
        Assert.Equal(84, hash.Length);
        Assert.True(PasswordSecurityHelper.IsHashedPassword(hash));
        Assert.True(PasswordSecurityHelper.IsIdentityV3(hash));
        Assert.False(PasswordSecurityHelper.IsIdentityV2(hash));
        Assert.False(PasswordSecurityHelper.IsPlainText(hash));
        Assert.Equal(PasswordHashVersion.IdentityV3, PasswordSecurityHelper.GetHashVersion(hash));
        Assert.Equal(PasswordClassification.IdentityV3, PasswordSecurityHelper.Classify(hash));
    }

    [Fact]
    public void PasswordHasher_RealProjectConfiguration_GeneratesV3Hash_ValidatesBinaryStructureAndClassification()
    {
        // 1. Instanciation réelle du hasher tel que configuré dans DependencyInjection.cs
        var user = new T_RESPONSABLE { IDRESPONSABLE = 10, LOGIN = "audit_user" };
        var rawHash = _hasher.HashPassword(user, "SuperAuditSecret!2026");

        Assert.NotNull(rawHash);
        Assert.Equal(84, rawHash.Length);

        // 2. Décodage binaire et inspection exhaustive des métadonnées
        var bytes = Convert.FromBase64String(rawHash);
        Assert.Equal(61, bytes.Length);

        // Octet 0 : Marqueur Identity V3 = 0x01
        Assert.Equal(0x01, bytes[0]);

        // Octets 1..4 : PRF = 2 (KeyDerivationPrf.HMACSHA512, standard .NET 8 par défaut)
        var prf = ((uint)bytes[1] << 24) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 8) | bytes[4];
        Assert.Equal(2u, prf);

        // Octets 5..8 : Nombre d'itérations = 100 000 en standard .NET 8
        var iterCount = ((uint)bytes[5] << 24) | ((uint)bytes[6] << 16) | ((uint)bytes[7] << 8) | bytes[8];
        Assert.Equal(100000u, iterCount);

        // Octets 9..12 : Longueur du sel cryptographique = 16 octets (128 bits)
        var saltLength = ((uint)bytes[9] << 24) | ((uint)bytes[10] << 16) | ((uint)bytes[11] << 8) | bytes[12];
        Assert.Equal(16u, saltLength);

        // Longueur de la sous-clé dérivée (reste du buffer) = 32 octets (256 bits)
        var subkeyLength = bytes.Length - 13 - (int)saltLength;
        Assert.Equal(32, subkeyLength);

        // 3. Compatibilité avec PasswordSecurityHelper.Classify
        Assert.Equal(PasswordClassification.IdentityV3, PasswordSecurityHelper.Classify(rawHash));
        Assert.Equal(PasswordHashVersion.IdentityV3, PasswordSecurityHelper.GetHashVersion(rawHash));
        Assert.True(PasswordSecurityHelper.IsHashedPassword(rawHash));
        Assert.True(PasswordSecurityHelper.IsIdentityV3(rawHash));
        Assert.False(PasswordSecurityHelper.IsIdentityV2(rawHash));
        Assert.False(PasswordSecurityHelper.IsPlainText(rawHash));

        // 4. Vérification du mot de passe
        var verifyResult = _hasher.VerifyHashedPassword(user, rawHash, "SuperAuditSecret!2026");
        Assert.Equal(PasswordVerificationResult.Success, verifyResult);
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV3HmacSha512_32ByteSubkey_ReturnsTrueAndIdentifiesV3()
    {
        // V3 avec HMAC-SHA512 (prf = 2) et sous-clé de 32 octets -> 61 octets binaires = 84 caractères Base64
        var hashSha512With32BSubkey = CreateIdentityV3CustomHash("sha512_32b_secret", prf: 2, subkeySize: 32);

        Assert.NotNull(hashSha512With32BSubkey);
        Assert.Equal(84, hashSha512With32BSubkey.Length);
        Assert.True(PasswordSecurityHelper.IsHashedPassword(hashSha512With32BSubkey));
        Assert.True(PasswordSecurityHelper.IsIdentityV3(hashSha512With32BSubkey));
        Assert.False(PasswordSecurityHelper.IsIdentityV2(hashSha512With32BSubkey));
        Assert.False(PasswordSecurityHelper.IsPlainText(hashSha512With32BSubkey));
        Assert.Equal(PasswordHashVersion.IdentityV3, PasswordSecurityHelper.GetHashVersion(hashSha512With32BSubkey));
        Assert.Equal(PasswordClassification.IdentityV3, PasswordSecurityHelper.Classify(hashSha512With32BSubkey));

        // Vérification avec PasswordHasher standard
        var user = new T_RESPONSABLE { IDRESPONSABLE = 4, LOGIN = "sha512_32b_user" };
        var verifyResult = _hasher.VerifyHashedPassword(user, hashSha512With32BSubkey, "sha512_32b_secret");
        Assert.True(verifyResult == PasswordVerificationResult.Success || verifyResult == PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV3HmacSha512_ReturnsTrueAndIdentifiesV3()
    {
        // V3 with HMAC-SHA512 (prf = 2, subkey = 64 bytes) -> 93 binary bytes = 124 Base64 chars
        var hash512 = CreateIdentityV3CustomHash("sha512_secret", prf: 2, subkeySize: 64);

        Assert.NotNull(hash512);
        Assert.Equal(124, hash512.Length);
        Assert.True(PasswordSecurityHelper.IsHashedPassword(hash512));
        Assert.True(PasswordSecurityHelper.IsIdentityV3(hash512));
        Assert.False(PasswordSecurityHelper.IsIdentityV2(hash512));
        Assert.False(PasswordSecurityHelper.IsPlainText(hash512));
        Assert.Equal(PasswordHashVersion.IdentityV3, PasswordSecurityHelper.GetHashVersion(hash512));
        Assert.Equal(PasswordClassification.IdentityV3, PasswordSecurityHelper.Classify(hash512));

        // Must also verify with ASP.NET Core PasswordHasher
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, LOGIN = "sha512_user" };
        var verifyResult = _hasher.VerifyHashedPassword(user, hash512, "sha512_secret");
        Assert.True(verifyResult == PasswordVerificationResult.Success || verifyResult == PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV2Hash_ReturnsTrueAndIdentifiesV2()
    {
        var v2Hash = CreateLegacyIdentityV2Hash("legacy_password");

        Assert.NotNull(v2Hash);
        Assert.Equal(68, v2Hash.Length);
        Assert.True(PasswordSecurityHelper.IsHashedPassword(v2Hash));
        Assert.True(PasswordSecurityHelper.IsIdentityV2(v2Hash));
        Assert.False(PasswordSecurityHelper.IsIdentityV3(v2Hash));
        Assert.False(PasswordSecurityHelper.IsPlainText(v2Hash));
        Assert.Equal(PasswordHashVersion.IdentityV2, PasswordSecurityHelper.GetHashVersion(v2Hash));
        Assert.Equal(PasswordClassification.IdentityV2, PasswordSecurityHelper.Classify(v2Hash));

        // Must verify with ASP.NET Core PasswordHasher (returns SuccessRehashNeeded)
        var user = new T_RESPONSABLE { IDRESPONSABLE = 3, LOGIN = "v2_user" };
        var verifyResult = _hasher.VerifyHashedPassword(user, v2Hash, "legacy_password");
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, verifyResult);
    }

    [Theory]
    [InlineData("5ebe2294ecd0e0f08eab7690d2a6ee69")] // MD5 32 hex chars
    [InlineData("2fd4e1c67a2d28fced849ee1bb76e7391b93eb12")] // SHA-1 40 hex chars
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // SHA-256 64 hex chars
    [InlineData("$2a$12$e8N4Yy9w/1z3o8hN7a5HXe8.J4vW8/x1t3y2w1z3o8hN7a5HXe8.J")] // BCrypt 60 chars
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$c29tZXNhbHQ$RdescudvJCsgqlrcrcnhbvMmOzdealYnk5GmvnOpqh")] // Argon2
    public void Classify_WithUnrecognizedExternalHashes_ReturnsUnrecognizedHashAndNeverPlainText(string externalHash)
    {
        var classification = PasswordSecurityHelper.Classify(externalHash);
        Assert.Equal(PasswordClassification.UnrecognizedHash, classification);
        Assert.False(PasswordSecurityHelper.IsHashedPassword(externalHash));
        Assert.False(PasswordSecurityHelper.IsPlainText(externalHash));
        Assert.Equal(PasswordHashVersion.None, PasswordSecurityHelper.GetHashVersion(externalHash));
    }

    [Fact]
    public void Classify_WithCorruptedOrTruncatedV3Hash_ReturnsAmbiguousOrMalformed()
    {
        // Truncated V3 hash
        var truncated = "AQAAAA" + new string('A', 20);
        Assert.Equal(PasswordClassification.AmbiguousOrMalformed, PasswordSecurityHelper.Classify(truncated));
        Assert.False(PasswordSecurityHelper.IsHashedPassword(truncated));
        Assert.False(PasswordSecurityHelper.IsPlainText(truncated));

        // 84 chars of Base64, but invalid PRF = 99
        var invalidPrfBytes = new byte[61];
        invalidPrfBytes[0] = 0x01;
        WriteNetworkByteOrder(invalidPrfBytes, 1, 99); // invalid PRF
        WriteNetworkByteOrder(invalidPrfBytes, 5, 100000);
        WriteNetworkByteOrder(invalidPrfBytes, 9, 16);
        var invalidPrfHash = Convert.ToBase64String(invalidPrfBytes);

        Assert.Equal(PasswordClassification.AmbiguousOrMalformed, PasswordSecurityHelper.Classify(invalidPrfHash));
        Assert.False(PasswordSecurityHelper.IsHashedPassword(invalidPrfHash));
        Assert.False(PasswordSecurityHelper.IsPlainText(invalidPrfHash));
    }

    [Fact]
    public void Classify_WithCorruptedV2Marker_ReturnsAmbiguousOrMalformed()
    {
        // 49 bytes of Base64, but invalid marker 0x05 instead of 0x00
        var invalidBytes = new byte[49];
        invalidBytes[0] = 0x05;
        var fakeHash = Convert.ToBase64String(invalidBytes);

        Assert.Equal(PasswordClassification.AmbiguousOrMalformed, PasswordSecurityHelper.Classify(fakeHash));
        Assert.False(PasswordSecurityHelper.IsHashedPassword(fakeHash));
        Assert.False(PasswordSecurityHelper.IsPlainText(fakeHash));
    }
}
