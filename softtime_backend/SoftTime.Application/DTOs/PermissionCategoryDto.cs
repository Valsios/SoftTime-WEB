namespace SoftTime.Application.DTOs;

/// <summary>Catégorie du catalogue des permissions (<c>GET /api/v1/privileges/catalog</c>).</summary>
public record PermissionCategoryDto(string Key, string Title, int? Gate, IReadOnlyList<PermissionItemDto> Items);
