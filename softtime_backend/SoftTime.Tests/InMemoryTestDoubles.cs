using SoftTime.Application.Abstractions.EmployeePlanning;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

internal sealed class FakeCompanyContext : ICompanyContext
{
    public string? SageDatabase { get; private set; } = "SAGE_TEST";
    public string? PointeuseDatabase { get; private set; } = "PTE_TEST";

    public void Set(string sage, string? pointeuse)
    {
        SageDatabase = sage;
        PointeuseDatabase = pointeuse;
    }
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public decimal? UserId => null;
    public string? Login => "test";
    public decimal? RoleId => 1;
    public string? Matricule => null;
    public IReadOnlyCollection<int> Rights => Array.Empty<int>();
    public bool HasRight(int droit) => true;
}

/// <summary>Source externe factice : pointages, badges, salariés (civilité) et événements SAGE.</summary>
internal sealed class FakeExternalSourceService : IExternalSourceService
{
    public List<ExternalPunchUser> Users { get; } = new();
    public List<ExternalPunch> Punches { get; } = new();
    public List<ExternalEmployee> Employees { get; } = new();
    public List<ExternalEvent> Events { get; } = new();
    public List<ExternalEmployeeEvent> EmployeeEvents { get; } = new();

    public Task<IReadOnlyList<ExternalEmployee>> GetEmployeesAsync(BddSage row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalEmployee>>(Employees.ToList());

    public Task<IReadOnlyList<ExternalAffectation>> GetAffectationsAsync(BddSage row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalAffectation>>(Array.Empty<ExternalAffectation>());

    public Task<IReadOnlyList<ExternalDepartment>> GetDepartmentsAsync(BddSage row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalDepartment>>(Array.Empty<ExternalDepartment>());

    public Task<IReadOnlyList<ExternalHoliday>> GetCompanyCalendarAsync(BddSage row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalHoliday>>(Array.Empty<ExternalHoliday>());

    public Task<IReadOnlyList<ExternalConstant>> GetConstantsAsync(BddSage row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalConstant>>(Array.Empty<ExternalConstant>());

    public Task<IReadOnlyList<ExternalEvent>> GetEventsAsync(BddSage row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalEvent>>(Events.ToList());

    public Task<IReadOnlyList<ExternalEmployeeEvent>> GetEmployeeEventsAsync(BddSage row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalEmployeeEvent>>(EmployeeEvents.ToList());

    public Task<IReadOnlyList<ExternalPunchUser>> GetPunchUsersAsync(BddPointeuse row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalPunchUser>>(Users.ToList());

    public Task<IReadOnlyList<ExternalPunch>> GetPunchesAsync(BddPointeuse row, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalPunch>>(Punches.ToList());
}

internal sealed class FakePlanningStatisticsReader : IPlanningStatisticsReader
{
    public Task<IReadOnlyList<PlanningDayCount>> CountPlannedDaysAsync(
        string sageDb, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PlanningDayCount>>(Array.Empty<PlanningDayCount>());
}
