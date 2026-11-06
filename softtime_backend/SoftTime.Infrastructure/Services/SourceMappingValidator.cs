using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class SourceMappingValidator : ISourceMappingValidator
{
    private readonly IUnitOfWork _uow;
    private readonly IExternalDiscoveryService _discovery;

    public SourceMappingValidator(
        IUnitOfWork uow,
        IExternalDiscoveryService discovery)
    {
        _uow = uow;
        _discovery = discovery;
    }

    public async Task ValidateMappingsAsync(bool isSage, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct)
    {
        var system = isSage ? "SAGE" : "POINTEUSE";
        var roles = await _uow.Repository<FieldRole>().ListAsync(r => r.SystemType == system, ct);
        foreach (var m in mappings.Where(x => !string.IsNullOrWhiteSpace(x.EntityKind)))
        {
            if (string.IsNullOrWhiteSpace(m.SourceTable))
                throw new DomainException($"La table source est requise pour '{m.EntityKind}'.");
            SourceIdentifiers.ValidateSourceIdentifier(m.SourceTable);
            var fields = m.Fields ?? Array.Empty<SourceFieldMappingDto>();
            foreach (var f in fields)
                SourceIdentifiers.ValidateSourceIdentifier(f.SourceColumn);
            var provided = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.SourceColumn))
                .Select(f => f.FieldRoleCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = roles
                .Where(r => r.EntityKind == m.EntityKind && r.IsRequired && !provided.Contains(r.Code))
                .Select(r => r.Code)
                .ToList();
            if (missing.Count > 0)
                throw new DomainException($"Champs obligatoires non mappés pour '{m.EntityKind}' : {string.Join(", ", missing)}.");
        }
    }

    public async Task<IReadOnlyList<string>> ValidateMappingColumnsAsync(string serveur, string nomBd, bool sqlAuth, string? login, string? password, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct)
    {
        var missing = new List<string>();
        foreach (var m in mappings.Where(x => !string.IsNullOrWhiteSpace(x.SourceTable)))
        {
            var cols = await _discovery.ListColumnsAsync(serveur, nomBd, m.SourceTable!, sqlAuth, login, password, ct);
            foreach (var col in (m.Fields ?? Array.Empty<SourceFieldMappingDto>())
                        .Where(f => !string.IsNullOrWhiteSpace(f.SourceColumn))
                        .Select(f => f.SourceColumn!))
            {
                if (!cols.Contains(col, StringComparer.OrdinalIgnoreCase))
                    missing.Add($"{m.SourceTable}.{col}");
            }
        }
        return missing;
    }
}
