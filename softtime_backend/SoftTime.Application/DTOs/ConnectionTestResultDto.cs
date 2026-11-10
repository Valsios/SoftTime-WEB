namespace SoftTime.Application.DTOs;

public record ConnectionTestResultDto(bool Success, string Message, IReadOnlyList<string>? MissingTables = null);
