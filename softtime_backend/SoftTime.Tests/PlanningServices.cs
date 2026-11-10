using SoftTime.Application.Abstractions.EmployeePlanning;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services.EmployeePlanning;
using SoftTime.Infrastructure.Services.PlanningImport;

namespace SoftTime.Tests;

/// <summary>Assemble les services de planning (ex-PlanningService) avec l'ancien constructeur et leur délègue chaque méthode.</summary>
internal sealed class PlanningServices
{
    public PlanningServices(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IPlanningStatisticsReader planningStats)
    {
        var filter = new PlanningDatabaseFilter(tenant);
        Shifts = new ShiftService(uow);
        EmployeePlanning = new EmployeePlanningService(uow, tenant, planningStats, filter);
        Imports = new PlanningImportService(uow, tenant, filter);
    }

    public ShiftService Shifts { get; }

    public EmployeePlanningService EmployeePlanning { get; }

    public PlanningImportService Imports { get; }

    public Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default)
        => Shifts.ListShiftsAsync(ct);

    public Task<PagedResult<ShiftDto>> ListShiftsPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default)
        => Shifts.ListShiftsPagedAsync(page, pageSize, search, ct);

    public Task<ShiftDto> SaveShiftAsync(ShiftDto dto, CancellationToken ct = default)
        => Shifts.SaveShiftAsync(dto, ct);

    public Task DeleteShiftAsync(decimal id, CancellationToken ct = default)
        => Shifts.DeleteShiftAsync(id, ct);

    public Task<ImportResultDto> ImportShiftsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
        => Shifts.ImportShiftsExcelAsync(rows, ct);

    public Task<IReadOnlyList<PlanningEmployeeDto>> ListEmployeesAsync(CancellationToken ct = default)
        => EmployeePlanning.ListEmployeesAsync(ct);

    public Task<IReadOnlyList<EmployeePlanningDto>> ListEmployeePlanningAsync(DateTime from, DateTime to, int? cardId, CancellationToken ct = default)
        => EmployeePlanning.ListEmployeePlanningAsync(from, to, cardId, ct);

    public Task<PagedResult<EmployeePlanningDto>> ListEmployeePlanningPagedAsync(EmployeePlanningQuery query, CancellationToken ct = default)
        => EmployeePlanning.ListEmployeePlanningPagedAsync(query, ct);

    public Task<IReadOnlyList<PlanningEmployeeStatDto>> ListEmployeeStatsAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => EmployeePlanning.ListEmployeeStatsAsync(from, to, ct);

    public Task<PagedResult<PlanningEmployeeStatDto>> ListEmployeeStatsPagedAsync(EmployeePlanningQuery query, CancellationToken ct = default)
        => EmployeePlanning.ListEmployeeStatsPagedAsync(query, ct);

    public Task SaveEmployeePlanningAsync(IReadOnlyCollection<EmployeePlanningDto> rows, CancellationToken ct = default)
        => EmployeePlanning.SaveEmployeePlanningAsync(rows, ct);

    public Task DeleteEmployeePlanningAsync(int id, CancellationToken ct = default)
        => EmployeePlanning.DeleteEmployeePlanningAsync(id, ct);

    public Task<ImportResultDto> ImportExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string> headers, CancellationToken ct = default)
        => Imports.ImportExcelAsync(rows, headers, ct);

    public Task<ImportResultDto> GenerateFromCategoryCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default)
        => Imports.GenerateFromCategoryCsvAsync(lines, ct);

    public Task<ImportResultDto> GenerateFromMatriculeCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default)
        => Imports.GenerateFromMatriculeCsvAsync(lines, ct);
}
