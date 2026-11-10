namespace SoftTime.Application.DTOs;

public record DiscoverTablesDto(string Serveur, string Base, bool SqlAuth, string? Login, string? Password, int? ConnectionId = null, string? ConnectionKind = null);
