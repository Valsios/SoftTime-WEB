namespace SoftTime.Application.Abstractions.ExternalData;

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
