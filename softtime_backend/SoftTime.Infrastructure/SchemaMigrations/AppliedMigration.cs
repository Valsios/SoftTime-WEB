namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Version déjà appliquée, lue dans dbo.T_SCHEMA_VERSION.</summary>
public sealed record AppliedMigration(int Version, string Name, string Checksum);
