using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.DataSources;

public interface ISourceMappingValidator
{
    Task ValidateMappingsAsync(bool isSage, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct);

    Task<IReadOnlyList<string>> ValidateMappingColumnsAsync(SqlServerCredentials credentials, string nomBd, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct);
}
