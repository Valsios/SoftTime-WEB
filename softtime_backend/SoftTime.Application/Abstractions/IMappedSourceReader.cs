namespace SoftTime.Application.Abstractions;

/// <summary>Ligne projetée depuis une source externe : valeur par code de rôle (FieldRole.Code).</summary>
public sealed record MappedRow(IReadOnlyDictionary<string, string?> Values)
{
    public string? this[string role] => Values.TryGetValue(role, out var v) ? v : null;
}

public enum MappedFilterKind { None, Equals, Range }

public sealed record MappedQuery(
    string? FilterColumn = null,
    MappedFilterKind FilterKind = MappedFilterKind.None,
    string? EqualsValue = null,
    string? RangeFrom = null,
    string? RangeTo = null,
    int? Top = null);

/// <summary>
/// Lecteur externe générique piloté par le mapping (rôle → colonne source).
/// Les identifiants de table/colonne sont validés avant interpolation ; les valeurs sont paramétrées.
/// </summary>
public interface IMappedSourceReader
{
    Task<IReadOnlyList<MappedRow>> ReadAsync(
        string connectionString,
        string sourceTable,
        IReadOnlyDictionary<string, string> roleToColumn,
        MappedQuery? query = null,
        CancellationToken cancellationToken = default);
}
