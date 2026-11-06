namespace SoftTime.Application.Services;

/// <summary>
/// Résultat détaillé d'une opération de migration des mots de passe.
/// Trace les comptes migrés, déjà sécurisés, vides ou nécessitant une attention administrative.
/// </summary>
public sealed record PasswordMigrationResult
{
    public int TotalScanned { get; init; }
    public int MigratedCount { get; init; }
    public int AlreadyHashedV3Count { get; init; }
    public int PreservedV2Count { get; init; }
    public int SkippedEmptyCount { get; init; }
    public int AmbiguousCount { get; init; }
    public List<AmbiguousAccountEntry> AmbiguousAccounts { get; init; } = new();
}
