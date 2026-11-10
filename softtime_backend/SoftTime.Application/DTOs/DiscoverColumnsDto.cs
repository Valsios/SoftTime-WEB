namespace SoftTime.Application.DTOs;

public record DiscoverColumnsDto(string Serveur, string Base, string Table, bool SqlAuth, string? Login, string? Password, int? ConnectionId = null, string? ConnectionKind = null);
