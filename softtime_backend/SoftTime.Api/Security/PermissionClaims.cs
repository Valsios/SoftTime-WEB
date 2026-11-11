using System.Security.Claims;

namespace SoftTime.Api.Security;

/// <summary>Claims de droits du jeton d'accès : permissions fines (séparées par des virgules) et empreinte des droits.</summary>
public static class PermissionClaims
{
    public const string Permissions = "permissions";
    public const string RightsStamp = "rights_stamp";

    public static string Join(IEnumerable<string> permissions) => string.Join(',', permissions);

    public static IReadOnlySet<string> ReadPermissions(ClaimsPrincipal principal)
        => (principal.FindFirst(Permissions)?.Value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string? ReadStamp(ClaimsPrincipal principal) => principal.FindFirst(RightsStamp)?.Value;
}
