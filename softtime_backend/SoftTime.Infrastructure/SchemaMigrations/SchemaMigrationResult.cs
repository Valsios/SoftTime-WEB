namespace SoftTime.Infrastructure.SchemaMigrations;

public sealed record SchemaMigrationResult(IReadOnlyList<string> Applied, IReadOnlyList<string> ChecksumMismatches)
{
    /// <summary>Scripts déjà enregistrés dans T_SCHEMA_VERSION (non rejoués).</summary>
    public int AlreadyUpToDate { get; init; }
}
