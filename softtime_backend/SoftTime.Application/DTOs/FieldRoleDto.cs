namespace SoftTime.Application.DTOs;

public record FieldRoleDto(string Code, string? Label, string SystemType, string EntityKind, bool IsRequired, string? AutoMappingPatterns, int SortOrder);
