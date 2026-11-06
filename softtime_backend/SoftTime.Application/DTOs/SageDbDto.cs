namespace SoftTime.Application.DTOs;

// Password : entrée seulement. Jamais renvoyé par l'API (PasswordDefined indique qu'un mot de passe est stocké).
public record SageDbDto(
    int Id,
    string? Serveur,
    string? Login,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Password,
    bool? SqlAuth,
    string? NomBd,
    string TypeBase = "STANDARD",
    IReadOnlyList<SourceEntityMappingDto>? Mappings = null,
    bool PasswordDefined = false)
{
    public SageDbDto() : this(default, default, default, default, default, default) { }
}
