namespace SoftTime.Infrastructure.SchemaMigrations;

public interface IMigrationScriptSource
{
    /// <summary>Scripts disponibles, triés par numéro de version croissant.</summary>
    IReadOnlyList<MigrationScript> Load();
}
