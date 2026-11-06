using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class AffectationImportService : IAffectationImportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public AffectationImportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<ImportResultDto> ImportAffectationsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var import = new AffectationImport(_uow, _tenant);
        await import.ChargerAsync(ct);
        foreach (var row in rows)
            await import.ImporterAsync(row, ct);
        await _uow.SaveChangesAsync(ct);
        return import.Resultat();
    }
}
