namespace SoftTime.Application.DTOs;

// ConnectionId/ConnectionKind : base existante ("SAGE" ou "POINTEUSE") dont le mot de passe stocké est utilisé si Password est vide.
public record DiscoverDatabasesDto(string Serveur, bool SqlAuth, string? Login, string? Password, int? ConnectionId = null, string? ConnectionKind = null);
