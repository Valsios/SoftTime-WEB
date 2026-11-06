using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>Assemble les services de pointage (ex-TimekeepingService) avec l'ancien constructeur et leur délègue chaque méthode.</summary>
internal sealed class TimekeepingServices
{
    public TimekeepingServices(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IHistoryArchiveService history)
    {
        var guard = new ValidatedWeekGuard(uow);
        Punches = new PunchService(uow, tenant, externalSource, history, guard);
        Anomalies = new AnomalyService(uow, tenant, history, guard);
        Corrections = new CorrectedHoursService(uow, tenant, guard);
    }

    public PunchService Punches { get; }

    public AnomalyService Anomalies { get; }

    public CorrectedHoursService Corrections { get; }

    public Task<IReadOnlyList<PunchDto>> ListPunchesAsync(PeriodRequest filter, CancellationToken ct = default)
        => Punches.ListPunchesAsync(filter, ct);

    public Task<PagedResult<PunchDto>> ListPunchesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
        => Punches.ListPunchesPagedAsync(filter, page, pageSize, ct);

    public Task<PunchDto> UpdatePunchAsync(decimal id, PunchUpdateDto dto, CancellationToken ct = default)
        => Punches.UpdatePunchAsync(id, dto, ct);

    public Task<PurgePunchesResultDto> PurgePunchesAsync(PeriodRequest request, CancellationToken ct = default)
        => Punches.PurgePunchesAsync(request, ct);

    public Task<ImportResultDto> ImportFromClockAsync(ImportPunchesRequest request, CancellationToken ct = default)
        => Punches.ImportFromClockAsync(request, ct);

    public Task<ImportResultDto> ImportPunchesExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
        => Punches.ImportPunchesExcelAsync(rows, ct);

    public Task<IReadOnlyList<AnomalyDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default)
        => Anomalies.ListAnomaliesAsync(filter, ct);

    public Task<PagedResult<AnomalyDto>> ListAnomaliesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
        => Anomalies.ListAnomaliesPagedAsync(filter, page, pageSize, ct);

    public Task<AnomalyDto?> GetAnomalyAsync(decimal id, CancellationToken ct = default)
        => Anomalies.GetAnomalyAsync(id, ct);

    public Task<AnomalyDto> UpdateAnomalyAsync(decimal id, AnomalyUpdateDto dto, CancellationToken ct = default)
        => Anomalies.UpdateAnomalyAsync(id, dto, ct);

    public Task<AnomalyValidationResultDto> ValidateAnomaliesAsync(IReadOnlyCollection<decimal> ids, CancellationToken ct = default)
        => Anomalies.ValidateAnomaliesAsync(ids, ct);

    public Task DeleteAnomalyAsync(decimal id, CancellationToken ct = default)
        => Anomalies.DeleteAnomalyAsync(id, ct);

    public Task<IReadOnlyList<CorrectedHourDto>> ListCorrectionsAsync(PeriodRequest filter, CancellationToken ct = default)
        => Corrections.ListCorrectionsAsync(filter, ct);

    public Task<PagedResult<CorrectedHourDto>> ListCorrectionsPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
        => Corrections.ListCorrectionsPagedAsync(filter, page, pageSize, ct);

    public Task<CorrectedHourDto> SaveCorrectionAsync(CorrectedHourDto dto, CancellationToken ct = default)
        => Corrections.SaveCorrectionAsync(dto, ct);

    public Task DeleteCorrectionAsync(decimal id, CancellationToken ct = default)
        => Corrections.DeleteCorrectionAsync(id, ct);

    public Task<ImportResultDto> ImportCorrectionsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
        => Corrections.ImportCorrectionsExcelAsync(rows, ct);
}
