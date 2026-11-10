using AutoMapper;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.DataSources;

public sealed class FieldRoleService : IFieldRoleService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ISourceMappingMigrator _sourceMappingMigrator;

    public FieldRoleService(
        IUnitOfWork uow,
        IMapper mapper,
        ISourceMappingMigrator sourceMappingMigrator)
    {
        _uow = uow;
        _mapper = mapper;
        _sourceMappingMigrator = sourceMappingMigrator;
    }

    public async Task<IReadOnlyList<FieldRoleDto>> GetFieldRolesAsync(string? systemType = null, CancellationToken ct = default)
    {
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        await _sourceMappingMigrator.MigrateLegacyMappingsAsync(ct);
        var list = await _uow.Repository<FieldRole>().ListAsync(_ => true, ct);
        return list
            .Where(r => string.IsNullOrWhiteSpace(systemType)
                        || string.Equals(r.SystemType, systemType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.SystemType)
            .ThenBy(r => r.EntityKind)
            .ThenBy(r => r.SortOrder)
            .Select(_mapper.Map<FieldRoleDto>)
            .ToList();
    }
}
