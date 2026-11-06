namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Correspondance entre un rôle de champ logique (T_FIELD_ROLE.Code) et la colonne source réelle.
/// </summary>
public class SourceFieldMapping
{
    public int Id { get; set; }

    public int EntityMappingId { get; set; }

    public string FieldRoleCode { get; set; } = string.Empty;

    public string? SourceColumn { get; set; }
}
