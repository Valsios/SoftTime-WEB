namespace SoftTime.Application.DTOs;

public record SourceEntityMappingDto(int Id, string? SystemType, int? SageDbId, int? PointeuseDbId, string EntityKind, string? SourceTable, IReadOnlyList<SourceFieldMappingDto> Fields)
{
    public SourceEntityMappingDto() : this(default, default, default, default, default!, default, Array.Empty<SourceFieldMappingDto>()) { }
}
