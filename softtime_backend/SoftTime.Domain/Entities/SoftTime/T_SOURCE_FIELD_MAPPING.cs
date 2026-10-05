using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Correspondance entre un rôle de champ logique (T_FIELD_ROLE.Code) et la colonne source réelle.
/// </summary>
[Table("T_SOURCE_FIELD_MAPPING")]
public class T_SOURCE_FIELD_MAPPING
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityMappingId { get; set; }

    public string FieldRoleCode { get; set; } = string.Empty;

    public string? SourceColumn { get; set; }
}
