using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Infrastructure.Repositories;

public class EfRepository<T> : IRepository<T> where T : class
{
    private readonly SoftTimeDbContext _db;
    private readonly DbSet<T> _set;

    public EfRepository(SoftTimeDbContext db)
    {
        _db = db;
        _set = db.Set<T>();
    }

    public IQueryable<T> Query() => _set.AsQueryable();

    public async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => await _set.FindAsync(new[] { id }, cancellationToken);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => _set.Where(predicate).ToListAsync(cancellationToken);

    public Task<List<T>> ListLatestAsync(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, DateTime?>> orderByDescending,
        int take,
        CancellationToken cancellationToken = default)
        => _set.Where(predicate).OrderByDescending(orderByDescending).Take(take).ToListAsync(cancellationToken);

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await _set.AddAsync(entity, cancellationToken);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        => await _set.AddRangeAsync(entities, cancellationToken);

    public void Update(T entity) => _set.Update(entity);

    public void Remove(T entity) => _set.Remove(entity);

    public void RemoveRange(IEnumerable<T> entities) => _set.RemoveRange(entities);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly SoftTimeDbContext _db;
    private readonly Dictionary<Type, object> _repos = new();

    public UnitOfWork(SoftTimeDbContext db) => _db = db;

    public IRepository<T> Repository<T>() where T : class
    {
        var type = typeof(T);
        if (_repos.TryGetValue(type, out var existing))
            return (IRepository<T>)existing;
        var repo = new EfRepository<T>(_db);
        _repos[type] = repo;
        return repo;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);

    public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default)
        => _db.Database.ExecuteSqlRawAsync(sql, cancellationToken: cancellationToken);
}
