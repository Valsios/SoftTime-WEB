namespace SoftTime.Application.Security;

/// <summary>Bases SAGE et pointeuse déclarées, et bases SAGE cochées dans « Accès bases » (T_BDD_AUTORISE) pour le compte.</summary>
public sealed record DatabaseCatalog(
    IReadOnlyCollection<DatabaseRef> Sage,
    IReadOnlyCollection<DatabaseRef> Pointeuse,
    IReadOnlyCollection<int> AuthorizedSage)
{
    public static DatabaseCatalog Empty { get; } = new(Array.Empty<DatabaseRef>(), Array.Empty<DatabaseRef>(), Array.Empty<int>());
}
