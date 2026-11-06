using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

public interface ISourceMappingValidator
{
    Task ValidateMappingsAsync(bool isSage, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct);

    Task<IReadOnlyList<string>> ValidateMappingColumnsAsync(string serveur, string nomBd, bool sqlAuth, string? login, string? password, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct);
}
