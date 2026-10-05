using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Catalogue (seedé) des rôles de champs logiques mappables depuis une source externe.
/// Un rôle est défini par le code applicatif : les administrateurs ne choisissent que la colonne source.
/// </summary>
[Table("T_FIELD_ROLE")]
public class T_FIELD_ROLE
{
    [Key]
    public string Code { get; set; } = string.Empty;

    public string? Label { get; set; }

    /// <summary>SAGE | POINTEUSE</summary>
    public string SystemType { get; set; } = "SAGE";

    /// <summary>EMPLOYEE | AFFECTATION | DEPARTMENT | COMPANY_CALENDAR | CONSTANT | ABSENCE_EVENT | EMPLOYEE_EVENT | PUNCH_USER | PUNCH</summary>
    public string EntityKind { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    /// <summary>Tokens séparés par '|' utilisés pour l'auto-mapping (ex: "badge|badgenumber").</summary>
    public string? AutoMappingPatterns { get; set; }

    public int SortOrder { get; set; }
}
