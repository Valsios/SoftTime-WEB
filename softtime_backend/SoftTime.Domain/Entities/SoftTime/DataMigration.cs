namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Transformation de données déjà appliquée (migration 006, table T_MIGRATION_DONNEES). Une transformation qui exige le
/// code de l'application (et non du SQL seul) s'y inscrit pour ne jamais être rejouée.
/// </summary>
public class DataMigration
{
    public string Code { get; set; } = string.Empty;

    public DateTime DateApplication { get; set; }
}
