using System.Security.Cryptography;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class PasswordHashClassifierTests
{
    private readonly PasswordHasher<Responsable> _hasher = new();

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
        PasswordHashClassifier.IsHashedPassword(null).Should().BeFalse();
        PasswordHashClassifier.IsHashedPassword("").Should().BeFalse();
        PasswordHashClassifier.IsHashedPassword("   ").Should().BeFalse();
        PasswordHashClassifier.GetHashVersion(null).Should().Be(PasswordHashVersion.None);
        PasswordHashClassifier.GetHashVersion("").Should().Be(PasswordHashVersion.None);
        PasswordHashClassifier.GetHashVersion("   ").Should().Be(PasswordHashVersion.None);
        PasswordHashClassifier.Classify(null).Should().Be(PasswordClassification.NullOrEmpty);
        PasswordHashClassifier.Classify("").Should().Be(PasswordClassification.NullOrEmpty);
        PasswordHashClassifier.Classify("   ").Should().Be(PasswordClassification.NullOrEmpty);
        PasswordHashClassifier.IsPlainText(null).Should().BeFalse();
        PasswordHashClassifier.IsPlainText("").Should().BeFalse();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("password123")]
    [InlineData("SuperSecret!2026")]
    [InlineData("azerty")]
    [InlineData("123456")]
    public void IsHashedPassword_WithPlainTextPasswords_ReturnsFalse(string plainText)
    {
        PasswordHashClassifier.IsHashedPassword(plainText).Should().BeFalse();
        PasswordHashClassifier.IsIdentityV2(plainText).Should().BeFalse();
        PasswordHashClassifier.IsIdentityV3(plainText).Should().BeFalse();
        PasswordHashClassifier.GetHashVersion(plainText).Should().Be(PasswordHashVersion.None);
        PasswordHashClassifier.Classify(plainText).Should().Be(PasswordClassification.LegacyPlainText);
        PasswordHashClassifier.IsPlainText(plainText).Should().BeTrue();
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV3StandardHash_ReturnsTrueAndIdentifiesV3()
    {
        var user = new Responsable { IdResponsable = 1, Login = "admin" };
        var hash = _hasher.HashPassword(user, "admin");

        hash.Should().NotBeNull();
        hash.Length.Should().Be(84);
        PasswordHashClassifier.IsHashedPassword(hash).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV3(hash).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV2(hash).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(hash).Should().BeFalse();
        PasswordHashClassifier.GetHashVersion(hash).Should().Be(PasswordHashVersion.IdentityV3);
        PasswordHashClassifier.Classify(hash).Should().Be(PasswordClassification.IdentityV3);
    }

    [Fact]
    public void PasswordHasher_RealProjectConfiguration_GeneratesV3Hash_ValidatesBinaryStructureAndClassification()
    {
        // 1. Instanciation réelle du hasher tel que configuré dans DependencyInjection.cs
        var user = new Responsable { IdResponsable = 10, Login = "audit_user" };
        var rawHash = _hasher.HashPassword(user, "SuperAuditSecret!2026");

        rawHash.Should().NotBeNull();
        rawHash.Length.Should().Be(84);

        // 2. Décodage binaire et inspection exhaustive des métadonnées
        var bytes = Convert.FromBase64String(rawHash);
        bytes.Length.Should().Be(61);

        // Octet 0 : Marqueur Identity V3 = 0x01
        bytes[0].Should().Be(0x01);

        // Octets 1..4 : PRF = 2 (KeyDerivationPrf.HMACSHA512, standard .NET 8 par défaut)
        var prf = ((uint)bytes[1] << 24) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 8) | bytes[4];
        prf.Should().Be(2u);

        // Octets 5..8 : Nombre d'itérations = 100 000 en standard .NET 8
        var iterCount = ((uint)bytes[5] << 24) | ((uint)bytes[6] << 16) | ((uint)bytes[7] << 8) | bytes[8];
        iterCount.Should().Be(100000u);

        // Octets 9..12 : Longueur du sel cryptographique = 16 octets (128 bits)
        var saltLength = ((uint)bytes[9] << 24) | ((uint)bytes[10] << 16) | ((uint)bytes[11] << 8) | bytes[12];
        saltLength.Should().Be(16u);

        // Longueur de la sous-clé dérivée (reste du buffer) = 32 octets (256 bits)
        var subkeyLength = bytes.Length - 13 - (int)saltLength;
        subkeyLength.Should().Be(32);

        // 3. Compatibilité avec PasswordHashClassifier.Classify
        PasswordHashClassifier.Classify(rawHash).Should().Be(PasswordClassification.IdentityV3);
        PasswordHashClassifier.GetHashVersion(rawHash).Should().Be(PasswordHashVersion.IdentityV3);
        PasswordHashClassifier.IsHashedPassword(rawHash).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV3(rawHash).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV2(rawHash).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(rawHash).Should().BeFalse();

        // 4. Vérification du mot de passe
        var verifyResult = _hasher.VerifyHashedPassword(user, rawHash, "SuperAuditSecret!2026");
        verifyResult.Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV3HmacSha512_32ByteSubkey_ReturnsTrueAndIdentifiesV3()
    {
        // V3 avec HMAC-SHA512 (prf = 2) et sous-clé de 32 octets -> 61 octets binaires = 84 caractères Base64
        var hashSha512With32BSubkey = CreateIdentityV3CustomHash("sha512_32b_secret", prf: 2, subkeySize: 32);

        hashSha512With32BSubkey.Should().NotBeNull();
        hashSha512With32BSubkey.Length.Should().Be(84);
        PasswordHashClassifier.IsHashedPassword(hashSha512With32BSubkey).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV3(hashSha512With32BSubkey).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV2(hashSha512With32BSubkey).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(hashSha512With32BSubkey).Should().BeFalse();
        PasswordHashClassifier.GetHashVersion(hashSha512With32BSubkey).Should().Be(PasswordHashVersion.IdentityV3);
        PasswordHashClassifier.Classify(hashSha512With32BSubkey).Should().Be(PasswordClassification.IdentityV3);

        // Vérification avec PasswordHasher standard
        var user = new Responsable { IdResponsable = 4, Login = "sha512_32b_user" };
        var verifyResult = _hasher.VerifyHashedPassword(user, hashSha512With32BSubkey, "sha512_32b_secret");
        (verifyResult == PasswordVerificationResult.Success || verifyResult == PasswordVerificationResult.SuccessRehashNeeded).Should().BeTrue();
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV3HmacSha512_ReturnsTrueAndIdentifiesV3()
    {
        // V3 with HMAC-SHA512 (prf = 2, subkey = 64 bytes) -> 93 binary bytes = 124 Base64 chars
        var hash512 = CreateIdentityV3CustomHash("sha512_secret", prf: 2, subkeySize: 64);

        hash512.Should().NotBeNull();
        hash512.Length.Should().Be(124);
        PasswordHashClassifier.IsHashedPassword(hash512).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV3(hash512).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV2(hash512).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(hash512).Should().BeFalse();
        PasswordHashClassifier.GetHashVersion(hash512).Should().Be(PasswordHashVersion.IdentityV3);
        PasswordHashClassifier.Classify(hash512).Should().Be(PasswordClassification.IdentityV3);

        // Must also verify with ASP.NET Core PasswordHasher
        var user = new Responsable { IdResponsable = 2, Login = "sha512_user" };
        var verifyResult = _hasher.VerifyHashedPassword(user, hash512, "sha512_secret");
        (verifyResult == PasswordVerificationResult.Success || verifyResult == PasswordVerificationResult.SuccessRehashNeeded).Should().BeTrue();
    }

    [Fact]
    public void IsHashedPassword_WithIdentityV2Hash_ReturnsTrueAndIdentifiesV2()
    {
        var v2Hash = CreateLegacyIdentityV2Hash("legacy_password");

        v2Hash.Should().NotBeNull();
        v2Hash.Length.Should().Be(68);
        PasswordHashClassifier.IsHashedPassword(v2Hash).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV2(v2Hash).Should().BeTrue();
        PasswordHashClassifier.IsIdentityV3(v2Hash).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(v2Hash).Should().BeFalse();
        PasswordHashClassifier.GetHashVersion(v2Hash).Should().Be(PasswordHashVersion.IdentityV2);
        PasswordHashClassifier.Classify(v2Hash).Should().Be(PasswordClassification.IdentityV2);

        // Must verify with ASP.NET Core PasswordHasher (returns SuccessRehashNeeded)
        var user = new Responsable { IdResponsable = 3, Login = "v2_user" };
        var verifyResult = _hasher.VerifyHashedPassword(user, v2Hash, "legacy_password");
        verifyResult.Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Theory]
    [InlineData("5ebe2294ecd0e0f08eab7690d2a6ee69")] // MD5 32 hex chars
    [InlineData("2fd4e1c67a2d28fced849ee1bb76e7391b93eb12")] // SHA-1 40 hex chars
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // SHA-256 64 hex chars
    [InlineData("$2a$12$e8N4Yy9w/1z3o8hN7a5HXe8.J4vW8/x1t3y2w1z3o8hN7a5HXe8.J")] // BCrypt 60 chars
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$c29tZXNhbHQ$RdescudvJCsgqlrcrcnhbvMmOzdealYnk5GmvnOpqh")] // Argon2
    public void Classify_WithUnrecognizedExternalHashes_ReturnsUnrecognizedHashAndNeverPlainText(string externalHash)
    {
        var classification = PasswordHashClassifier.Classify(externalHash);
        classification.Should().Be(PasswordClassification.UnrecognizedHash);
        PasswordHashClassifier.IsHashedPassword(externalHash).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(externalHash).Should().BeFalse();
        PasswordHashClassifier.GetHashVersion(externalHash).Should().Be(PasswordHashVersion.None);
    }

    [Fact]
    public void Classify_WithCorruptedOrTruncatedV3Hash_ReturnsAmbiguousOrMalformed()
    {
        // Truncated V3 hash
        var truncated = "AQAAAA" + new string('A', 20);
        PasswordHashClassifier.Classify(truncated).Should().Be(PasswordClassification.AmbiguousOrMalformed);
        PasswordHashClassifier.IsHashedPassword(truncated).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(truncated).Should().BeFalse();

        // 84 chars of Base64, but invalid PRF = 99
        var invalidPrfBytes = new byte[61];
        invalidPrfBytes[0] = 0x01;
        WriteNetworkByteOrder(invalidPrfBytes, 1, 99); // invalid PRF
        WriteNetworkByteOrder(invalidPrfBytes, 5, 100000);
        WriteNetworkByteOrder(invalidPrfBytes, 9, 16);
        var invalidPrfHash = Convert.ToBase64String(invalidPrfBytes);

        PasswordHashClassifier.Classify(invalidPrfHash).Should().Be(PasswordClassification.AmbiguousOrMalformed);
        PasswordHashClassifier.IsHashedPassword(invalidPrfHash).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(invalidPrfHash).Should().BeFalse();
    }

    [Fact]
    public void Classify_WithCorruptedV2Marker_ReturnsAmbiguousOrMalformed()
    {
        // 49 bytes of Base64, but invalid marker 0x05 instead of 0x00
        var invalidBytes = new byte[49];
        invalidBytes[0] = 0x05;
        var fakeHash = Convert.ToBase64String(invalidBytes);

        PasswordHashClassifier.Classify(fakeHash).Should().Be(PasswordClassification.AmbiguousOrMalformed);
        PasswordHashClassifier.IsHashedPassword(fakeHash).Should().BeFalse();
        PasswordHashClassifier.IsPlainText(fakeHash).Should().BeFalse();
    }
}
