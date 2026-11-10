namespace SoftTime.Application.Security;

/// <summary>Bases connues (noms) et bases cochées dans « Accès bases » (T_BDD_AUTORISE) pour l'utilisateur.</summary>
public sealed record DatabaseCatalog(
    IReadOnlyCollection<string> Sage,
    IReadOnlyCollection<string> Pointeuse,
    IReadOnlyCollection<string> AuthorizedSage)
{
    public static DatabaseCatalog Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}
