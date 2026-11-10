using System.Security.Cryptography;
using System.Text;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Script versionné de migrations/versions (NNN_description.sql).</summary>
public sealed record MigrationScript(int Version, string Name, string Content)
{
    /// <summary>
    /// SHA-256 hexadécimal du script. Les fins de ligne sont normalisées en LF avant le calcul :
    /// un même fichier extrait en CRLF (Windows) ou en LF donne la même somme de contrôle.
    /// </summary>
    public string Checksum { get; } = ComputeChecksum(Content);

    public static string ComputeChecksum(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
