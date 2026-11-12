namespace SoftTime.Application.DTOs;

/// <summary>Écran du catalogue des permissions : code de page et actions proposées.</summary>
public record PermissionItemDto(string Code, string Label, bool CanCreate, bool CanUpdate, bool CanDelete);
