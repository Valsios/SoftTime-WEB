using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Mapping d'une entité logique (employé, affectation, pointage, ...) vers une table source externe
/// pour une connexion donnée (Sage ou Pointeuse). Exactement une des deux FK est renseignée.
/// </summary>
[Table("T_SOURCE_ENTITY_MAPPING")]
public class T_SOURCE_ENTITY_MAPPING
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>SAGE | POINTEUSE</summary>
    public string SystemType { get; set; } = "SAGE";

    public int? SageDbId { get; set; }
    public int? PointeuseDbId { get; set; }

    public string EntityKind { get; set; } = string.Empty;

    public string? SourceTable { get; set; }

    public List<T_SOURCE_FIELD_MAPPING> Fields { get; set; } = new();
}
