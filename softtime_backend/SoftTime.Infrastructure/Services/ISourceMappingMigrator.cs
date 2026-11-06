using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public interface ISourceMappingMigrator
{
    Task EnsureMappingSchemaAsync(CancellationToken ct);

    Task MigrateLegacyMappingsAsync(CancellationToken ct);

    Task AddLegacyFieldsAsync(IRepository<SourceFieldMapping> repo, int entityMappingId, (string Role, string? Column)[] pairs, CancellationToken ct);
}
