namespace SoftTime.Application.DTOs;

public record PointeuseDbDto(
    int Id,
    string? Serveur,
    string? Login,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Password,
    bool? SqlAuth,
    string? NomBd,
    string? TypePointage,
    bool? Active,
    string TypeBase = "STANDARD",
    IReadOnlyList<SourceEntityMappingDto>? Mappings = null,
    bool PasswordDefined = false) : IExternalConnectionFields
{
    public PointeuseDbDto() : this(default, default, default, default, default, default, default, default) { }
}
