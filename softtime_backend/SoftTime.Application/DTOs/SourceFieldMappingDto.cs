namespace SoftTime.Application.DTOs;

public record SourceFieldMappingDto(int Id, string FieldRoleCode, string? SourceColumn)
{
    public SourceFieldMappingDto() : this(default, default!, default) { }
}
