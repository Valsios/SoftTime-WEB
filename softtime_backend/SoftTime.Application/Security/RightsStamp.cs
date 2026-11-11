using System.Security.Cryptography;
using System.Text;

namespace SoftTime.Application.Security;

/// <summary>
/// Empreinte des droits effectifs d'un compte, écrite dans le jeton d'accès. Dès que les droits changent, l'empreinte
/// change : le jeton est alors périmé et le client doit le renouveler. Calcul pur et déterministe.
/// </summary>
public static class RightsStamp
{
    private const int StampLength = 16;

    public static string Compute(EffectiveRights rights)
    {
        var content = string.Join('|',
            rights.IsAdministrator ? "A" : "U",
            Normalize(rights.Permissions),
            string.Join(',', rights.Gates.OrderBy(gate => gate)),
            string.Join(',', rights.AuthorizedSageDatabases.OrderBy(id => id)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..StampLength];
    }

    /// <summary>Vrai si l'empreinte du jeton est celle des droits actuels (une empreinte absente n'est jamais à jour).</summary>
    public static bool IsCurrent(string? tokenStamp, EffectiveRights rights)
        => !string.IsNullOrEmpty(tokenStamp) && string.Equals(tokenStamp, rights.Stamp, StringComparison.Ordinal);

    private static string Normalize(IEnumerable<string> values)
        => string.Join(',', values.Select(value => value.ToUpperInvariant()).OrderBy(value => value, StringComparer.Ordinal));
}
