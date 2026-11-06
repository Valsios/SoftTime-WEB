using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>Assemble les services des heures supplémentaires (ex-OvertimeService) avec l'ancien constructeur et leur délègue chaque méthode.</summary>
internal sealed class OvertimeServices
{
    public OvertimeServices(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IPayrollWriterResolver payrollWriters,
        IHistoryArchiveService history,
        ISageLeaveCalendar leaves,
        ICurrentUser? user = null)
    {
        var context = new OvertimeWeekContext(uow, tenant, externalSource, leaves);
        WeeklyValidation = new WeeklyValidationService(uow, tenant, context);
        ExoImpo = new OvertimeExoImpoService(uow, tenant, externalSource, context);
        Detail = new OvertimeDetailService(uow, tenant, history, context);
        PayrollSync = new OvertimePayrollSyncService(uow, tenant, payrollWriters, user, context);
    }

    public WeeklyValidationService WeeklyValidation { get; }

    public OvertimeExoImpoService ExoImpo { get; }

    public OvertimeDetailService Detail { get; }

    public OvertimePayrollSyncService PayrollSync { get; }

    public Task<IReadOnlyList<WeeklyHsDto>> PreviewWeekAsync(PeriodRequest request, CancellationToken ct = default)
        => WeeklyValidation.PreviewWeekAsync(request, ct);

    public Task<PagedResult<WeeklyHsDto>> PreviewWeekPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
        => WeeklyValidation.PreviewWeekPagedAsync(request, page, pageSize, ct);

    public Task ValidateWeekAsync(WeeklyValidationRequest request, bool validate, CancellationToken ct = default)
        => WeeklyValidation.ValidateWeekAsync(request, validate, ct);

    public Task<PurgeValidationResultDto> PurgeValidationAsync(PeriodRequest request, CancellationToken ct = default)
        => WeeklyValidation.PurgeValidationAsync(request, ct);

    public Task<IReadOnlyList<HsExoDto>> ListExoAsync(PeriodRequest request, CancellationToken ct = default)
        => ExoImpo.ListExoAsync(request, ct);

    public Task<PagedResult<HsExoDto>> ListExoPagedAsync(PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
        => ExoImpo.ListExoPagedAsync(request, page, pageSize, ct);

    public Task<IReadOnlyList<HsExoDto>> CalculateExoImpoAsync(PeriodRequest request, CancellationToken ct = default)
        => ExoImpo.CalculateExoImpoAsync(request, ct);

    public Task PurgeHsAsync(PeriodRequest request, CancellationToken ct = default)
        => Detail.PurgeHsAsync(request, ct);

    public Task<OvertimeDetailDto> DetailAsync(string matricule, DateTime from, DateTime to, CancellationToken ct = default)
        => Detail.DetailAsync(matricule, from, to, ct);

    public Task SyncSageAsync(PeriodRequest request, CancellationToken ct = default)
        => PayrollSync.SyncSageAsync(request, ct);

    public Task<PayrollWriteResultDto> SyncPayrollAsync(PeriodRequest request, CancellationToken ct = default)
        => PayrollSync.SyncPayrollAsync(request, ct);
}
