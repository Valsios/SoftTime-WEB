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
            case Pointage pointage when pointage.IdPointage == 0:
                pointage.IdPointage = _nextId++;
                break;
            case HeureAnomalie anomalie when anomalie.Id == 0:
                anomalie.Id = _nextId++;
                break;
            case HeureCorrigee correction when correction.Id == 0:
                correction.Id = _nextId++;
                break;
            case CardPaie carte when carte.Id == 0:
                carte.Id = (int)_nextId++;
                break;
            case PlanningSalarie planning when planning.Id == 0:
                planning.Id = (int)_nextId++;
                break;
            case ParametrePointeuse clock when clock.IdPoint == 0:
                clock.IdPoint = (int)_nextId++;
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
