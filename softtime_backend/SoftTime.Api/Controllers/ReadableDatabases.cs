using SoftTime.Application.Security;
using SoftTime.Application.Services;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Bases qu'un compte personnalisé peut lire, d'après ses permissions : null pour toutes (joker « sage.all.read »),
/// sinon les identifiants lus (« sage.12.read »).
/// </summary>
internal static class ReadableDatabases
{
    public static IReadOnlyCollection<int>? ReadableIds(IEnumerable<string> permissions, DatabaseKind kind)
    {
        var ids = new HashSet<int>();
        foreach (var code in permissions)
        {
            if (!DatabasePermissionCodes.TryParse(code, out var k, out var target, out var action)
                || k != kind || action != DatabasePermissionCodes.Read)
                continue;
            if (target == DatabasePermissionCodes.All)
                return null;
            ids.Add(int.Parse(target!, System.Globalization.CultureInfo.InvariantCulture));
        }
        return ids;
    }
}
