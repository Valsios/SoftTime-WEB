using System.Linq.Expressions;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Tests;

/// <summary>Doublures en mémoire pour tester les services sans SQL Server.</summary>
internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repos = new();

    public InMemoryRepository<T> Get<T>() where T : class
    {
        if (_repos.TryGetValue(typeof(T), out var existing))
            return (InMemoryRepository<T>)existing;
        var repo = new InMemoryRepository<T>();
        _repos[typeof(T)] = repo;
        return repo;
    }

    public IRepository<T> Repository<T>() where T : class => Get<T>();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

    public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class InMemoryRepository<T> : IRepository<T> where T : class
{
    private decimal _nextId = 1;

    public List<T> Items { get; } = new();

    public IQueryable<T> Query() => Items.AsQueryable();

    public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault());

    public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Where(predicate.Compile()).ToList());

    public Task<List<T>> ListLatestAsync(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, DateTime?>> orderByDescending,
        int take,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items
            .Where(predicate.Compile())
            .OrderByDescending(orderByDescending.Compile())
            .Take(take)
            .ToList());

    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        AssignId(entity);
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
            AddAsync(entity, cancellationToken);
        return Task.CompletedTask;
    }

    public void Update(T entity) { }

    public void Remove(T entity) => Items.Remove(entity);

    public void RemoveRange(IEnumerable<T> entities)
    {
        foreach (var entity in entities.ToList())
            Items.Remove(entity);
    }

    private void AssignId(T entity)
    {
        switch (entity)
        {
            case T_POINTAGE pointage when pointage.IDPOINTAGE == 0:
                pointage.IDPOINTAGE = _nextId++;
                break;
            case T_HEUREANOMALIE anomalie when anomalie.ID == 0:
                anomalie.ID = _nextId++;
                break;
            case T_HEURECORRIGER correction when correction.ID == 0:
                correction.ID = _nextId++;
                break;
            case T_CARDPAIE carte when carte.ID == 0:
                carte.ID = (int)_nextId++;
                break;
            case T_PLANNING_SAL planning when planning.ID == 0:
                planning.ID = (int)_nextId++;
                break;
            case T_CLOCK clock when clock.IDPOINT == 0:
                clock.IDPOINT = (int)_nextId++;
                break;
        }
    }
}

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

    public Task<IReadOnlyList<ExternalEmployee>> GetEmployeesAsync(T_BDD_SAGE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalEmployee>>(Employees.ToList());

    public Task<IReadOnlyList<ExternalAffectation>> GetAffectationsAsync(T_BDD_SAGE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalAffectation>>(Array.Empty<ExternalAffectation>());

    public Task<IReadOnlyList<ExternalDepartment>> GetDepartmentsAsync(T_BDD_SAGE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalDepartment>>(Array.Empty<ExternalDepartment>());

    public Task<IReadOnlyList<ExternalHoliday>> GetCompanyCalendarAsync(T_BDD_SAGE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalHoliday>>(Array.Empty<ExternalHoliday>());

    public Task<IReadOnlyList<ExternalConstant>> GetConstantsAsync(T_BDD_SAGE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalConstant>>(Array.Empty<ExternalConstant>());

    public Task<IReadOnlyList<ExternalEvent>> GetEventsAsync(T_BDD_SAGE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalEvent>>(Events.ToList());

    public Task<IReadOnlyList<ExternalEmployeeEvent>> GetEmployeeEventsAsync(T_BDD_SAGE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalEmployeeEvent>>(EmployeeEvents.ToList());

    public Task<IReadOnlyList<ExternalPunchUser>> GetPunchUsersAsync(T_BDD_POINTEUSE row, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalPunchUser>>(Users.ToList());

    public Task<IReadOnlyList<ExternalPunch>> GetPunchesAsync(T_BDD_POINTEUSE row, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExternalPunch>>(Punches.ToList());
}

internal sealed class FakePlanningStatisticsReader : IPlanningStatisticsReader
{
    public Task<IReadOnlyList<PlanningDayCount>> CountPlannedDaysAsync(
        string sageDb, DateTime from, DateTime to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PlanningDayCount>>(Array.Empty<PlanningDayCount>());
}
