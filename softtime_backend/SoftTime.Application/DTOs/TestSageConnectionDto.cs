namespace SoftTime.Application.DTOs;

public record TestSageConnectionDto(string? Serveur, bool? SqlAuth, string? Login, string? Password, string? NomBd, string TypeBase, IReadOnlyList<SourceEntityMappingDto>? Mappings = null, int? Id = null);
