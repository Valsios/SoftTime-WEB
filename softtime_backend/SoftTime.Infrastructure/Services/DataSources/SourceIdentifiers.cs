using System.Text.RegularExpressions;

namespace SoftTime.Infrastructure.Services.DataSources;

internal static class SourceIdentifiers
{

    internal static readonly Regex SafeSourceIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    internal static void ValidateSourceIdentifier(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !SafeSourceIdentifier.IsMatch(name))
            throw new DomainException($"Identifiant source invalide : {name}");
    }
}
