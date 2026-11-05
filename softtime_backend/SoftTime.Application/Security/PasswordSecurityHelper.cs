namespace SoftTime.Application.Security;

public enum PasswordHashVersion
{
    None = 0,
    IdentityV2 = 2,
    IdentityV3 = 3
}

/// <summary>
/// Classification fine des valeurs stockées dans la colonne PASSWORD.
/// Permet de distinguer avec précision les hachages supportés, les formats inconnus,
/// les valeurs corrompues et les mots de passe historiques en clair.
/// </summary>
public enum PasswordClassification
{
    NullOrEmpty = 0,
    IdentityV3 = 1,
    IdentityV2 = 2,
    LegacyPlainText = 3,
    UnrecognizedHash = 4,
    AmbiguousOrMalformed = 5
}

/// <summary>
/// Utilitaires de sécurité pour la gestion, la vérification et la migration des mots de passe.
/// Analyse la structure binaire des hachages ASP.NET Core Identity (V2 et V3)
/// et protège rigoureusement les valeurs ambiguës ou de formats inconnus (MD5, SHA, BCrypt)
/// contre toute corruption ou double-hachage involontaire.
/// </summary>
public static class PasswordSecurityHelper
{
    /// <summary>
    /// Classe de manière robuste et déterministe la valeur stockée.
    /// </summary>
    public static PasswordClassification Classify(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return PasswordClassification.NullOrEmpty;

        var trimmed = password.Trim();

        // 1. Détection des formats MCF (Modular Crypt Format) et formats reconnus d'autres systèmes :
        // BCrypt ($2a$, $2b$, $2y$), Argon2 ($argon2), PBKDF2 ($pbkdf2), Unix Crypt ($1$, $5$, $6$), LDAP ({SSHA}, etc.)
        if (IsModularCryptFormat(trimmed))
            return PasswordClassification.UnrecognizedHash;

        // 2. Détection des empreintes hexadécimales pures (MD5 = 32, SHA-1 = 40, SHA-256 = 64, SHA-512 = 128)
        if (IsHexHash(trimmed))
            return PasswordClassification.UnrecognizedHash;

        // 3. Détection des formats Identity V2 et V3 par inspection de la structure binaire décodée
        if (trimmed.Length >= 4 && trimmed.Length % 4 == 0 && IsBase64Chars(trimmed))
        {
            try
            {
                Span<byte> buffer = stackalloc byte[trimmed.Length];
                if (Convert.TryFromBase64String(trimmed, buffer, out var bytesWritten))
                {
                    // Format Identity V2 (ASP.NET Identity 1.x / 2.x) :
                    // 49 octets = 0x00 (header) + 16 octets salt + 32 octets subkey (HMAC-SHA1).
                    // Représentation Base64 exacte = 68 caractères.
                    if (bytesWritten == 49 && buffer[0] == 0x00 && trimmed.Length == 68)
                    {
                        return PasswordClassification.IdentityV2;
                    }

                    // Format Identity V3 (ASP.NET Core Identity) :
                    // Octet 0 : 0x01 (marqueur V3)
                    // Octets 1..4 : PRF (uint big-endian : 0=HMACSHA1, 1=HMACSHA256, 2=HMACSHA512)
                    // Octets 5..8 : Nombre d'itérations (uint big-endian >= 1)
                    // Octets 9..12 : Taille du sel en octets (uint big-endian >= 16)
                    // Octets 13..13+saltLength-1 : Sel
                    // Octets restants : Sous-clé PBKDF2 (>= 16 octets)
                    // Longueur totale minimale = 1 + 4 + 4 + 4 + 16 + 16 = 45 octets binaires.
                    // Cas standard .NET 8 (HMAC-SHA256, 128b salt, 256b subkey) = 61 octets = 84 caractères Base64.
                    // Cas HMAC-SHA512 (512b subkey) = 93 octets = 124 caractères Base64.
                    if (bytesWritten >= 45 && buffer[0] == 0x01)
                    {
                        var prf = ReadNetworkByteOrder(buffer, 1);
                        var iterCount = ReadNetworkByteOrder(buffer, 5);
                        var saltLength = ReadNetworkByteOrder(buffer, 9);

                        if (prf <= 2 && iterCount >= 1 && saltLength >= 16 && bytesWritten >= 13 + saltLength + 16)
                        {
                            return PasswordClassification.IdentityV3;
                        }

                        // Commençait par le marqueur V3 (0x01) mais avec des métadonnées invalides ou tronquées
                        return PasswordClassification.AmbiguousOrMalformed;
                    }

                    // Commençait par un marqueur Identity (0x00 ou 0x01) mais taille ou structure invalide
                    if (bytesWritten > 0 && (buffer[0] == 0x00 || buffer[0] == 0x01))
                    {
                        return PasswordClassification.AmbiguousOrMalformed;
                    }

                    // Digest binaire brut encodé en Base64 (MD5=16B, SHA-1=20B, SHA-256=32B, SHA-512=64B)
                    if ((bytesWritten == 16 || bytesWritten == 20 || bytesWritten == 32 || bytesWritten == 64)
                        && trimmed.EndsWith('='))
                    {
                        return PasswordClassification.UnrecognizedHash;
                    }

                    // Chaîne Base64 longue avec padding '=' non reconnue : potentielle empreinte inconnue ou corrompue
                    if (trimmed.Length >= 40 && trimmed.EndsWith('='))
                    {
                        return PasswordClassification.AmbiguousOrMalformed;
                    }
                }
            }
            catch
            {
                // En cas d'erreur de décodage, continuer vers l'analyse de forme
            }
        }

        // 4. Détection des marqueurs de hachages altérés ou tronqués
        if (LooksLikeMalformedHash(trimmed))
        {
            return PasswordClassification.AmbiguousOrMalformed;
        }

        // 5. Mot de passe historique en texte clair avéré
        return PasswordClassification.LegacyPlainText;
    }

    /// <summary>
    /// Détermine la version de hachage d'une chaîne stockée (V2, V3 ou Aucun).
    /// </summary>
    public static PasswordHashVersion GetHashVersion(string? password)
    {
        var classification = Classify(password);
        return classification switch
        {
            PasswordClassification.IdentityV2 => PasswordHashVersion.IdentityV2,
            PasswordClassification.IdentityV3 => PasswordHashVersion.IdentityV3,
            _ => PasswordHashVersion.None
        };
    }

    /// <summary>
    /// Détermine si une chaîne stockée correspond à un hachage valide produit par PasswordHasher (V2 ou V3).
    /// </summary>
    public static bool IsHashedPassword(string? password)
    {
        var version = GetHashVersion(password);
        return version == PasswordHashVersion.IdentityV2 || version == PasswordHashVersion.IdentityV3;
    }

    /// <summary>
    /// Détermine si la chaîne correspond à un hachage Identity V2 (HMAC-SHA1 hérité).
    /// </summary>
    public static bool IsIdentityV2(string? password)
        => Classify(password) == PasswordClassification.IdentityV2;

    /// <summary>
    /// Détermine si la chaîne correspond à un hachage Identity V3 (PBKDF2 standard .NET 8).
    /// </summary>
    public static bool IsIdentityV3(string? password)
        => Classify(password) == PasswordClassification.IdentityV3;

    /// <summary>
    /// Détermine avec certitude si la chaîne est un mot de passe en texte clair hérité,
    /// excluant les hachages valides, les formats inconnus (MD5, BCrypt) et les valeurs corrompues.
    /// </summary>
    public static bool IsPlainText(string? password)
        => Classify(password) == PasswordClassification.LegacyPlainText;

    private static uint ReadNetworkByteOrder(ReadOnlySpan<byte> buffer, int offset)
    {
        return ((uint)buffer[offset] << 24)
            | ((uint)buffer[offset + 1] << 16)
            | ((uint)buffer[offset + 2] << 8)
            | ((uint)buffer[offset + 3]);
    }

    private static bool IsBase64Chars(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '='))
                return false;
        }
        return true;
    }

    private static bool IsHexHash(string s)
    {
        if (s.Length != 32 && s.Length != 40 && s.Length != 64 && s.Length != 128)
            return false;

        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    private static bool IsModularCryptFormat(string s)
    {
        return s.StartsWith("$2a$", StringComparison.Ordinal)
            || s.StartsWith("$2b$", StringComparison.Ordinal)
            || s.StartsWith("$2y$", StringComparison.Ordinal)
            || s.StartsWith("$2x$", StringComparison.Ordinal)
            || s.StartsWith("$argon2", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("$pbkdf2", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("$1$", StringComparison.Ordinal)
            || s.StartsWith("$5$", StringComparison.Ordinal)
            || s.StartsWith("$6$", StringComparison.Ordinal)
            || s.StartsWith("{SSHA}", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("{SHA}", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("{MD5}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMalformedHash(string s)
    {
        // En-tête Base64 Identity V3 typique mais valeur corrompue, tronquée ou invalide
        if (s.StartsWith("AQAA", StringComparison.Ordinal) && (s.Length < 68 || s.Length % 4 != 0 || !IsBase64Chars(s)))
            return true;

        // Préfixe de type crypt ($...) avec séparateurs multiples
        if (s.StartsWith('$') && s.Count(c => c == '$') >= 2)
            return true;

        return false;
    }
}
