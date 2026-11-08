namespace SoftTime.Application.DTOs;

public record TestPointeuseConnectionDto(string? Serveur, bool? SqlAuth, string? Login, string? Password, string? NomBd, string TypeBase, IReadOnlyList<SourceEntityMappingDto>? Mappings = null, int? Id = null) : IExternalConnectionFields;
