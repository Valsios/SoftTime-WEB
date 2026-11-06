using System.Linq.Expressions;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using SoftTime.Application.Security;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
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

    public Task<int> CountAsync<TResult>(IQueryable<TResult> query, CancellationToken cancellationToken = default)
        => EntityFrameworkQueryableExtensions.CountAsync(query ?? throw new ArgumentNullException(nameof(query)), cancellationToken);

    public Task<List<TResult>> ToListAsync<TResult>(IQueryable<TResult> query, CancellationToken cancellationToken = default)
        => EntityFrameworkQueryableExtensions.ToListAsync(query ?? throw new ArgumentNullException(nameof(query)), cancellationToken);

    public async Task<PagedResult<TResult>> ToPagedAsync<TResult>(
        IQueryable<TResult> query,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var total = await query.CountAsync(cancellationToken);
        var (pageNorm, taille) = PaginationHelper.CalculerPagination(page, pageSize);

        if (total == 0)
        {
            return new PagedResult<TResult>(
                Array.Empty<TResult>(),
                0,
                pageNorm,
                taille,
                1);
        }

        var totalPages = PaginationRules.NombreDePages(total, taille);
        if (pageNorm > totalPages)
        {
            return new PagedResult<TResult>(
                Array.Empty<TResult>(),
                total,
                pageNorm,
                taille,
                totalPages);
        }

        var items = await query
            .Skip(PaginationRules.Decalage(pageNorm, taille))
            .Take(taille)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(
            items,
            total,
            pageNorm,
            taille,
            totalPages);
    }

    public async Task<PagedResult<TDto>> ToPagedAsync<TDto>(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        Expression<Func<T, TDto>> selector,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(selector);

        IQueryable<T> query = _set.AsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        var projected = ordered.Select(selector);
        return await ToPagedAsync(projected, page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<T>> ToPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderBy);

        IQueryable<T> query = _set.AsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy(query);
        return await ToPagedAsync(ordered, page, pageSize, cancellationToken);
    }

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

    /// <summary>Délai d'attente du verrou de <see cref="RunExclusiveAsync{TResult}"/>.</summary>
    private const int ExclusiveLockTimeoutMs = 15_000;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsLoginUniqueViolation(ex))
        {
            // Deux enregistrements simultanés du même login : le second est refusé par l'index unique
            // (migration 005) après avoir passé la vérification applicative. Même réponse que cette vérification.
            throw new FieldValidationException("login", LoginKey.TakenMessage);
        }
    }

    public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default)
        => _db.Database.ExecuteSqlRawAsync(sql, cancellationToken: cancellationToken);

    public Task<TResult> RunExclusiveAsync<TResult>(
        string resource,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentNullException.ThrowIfNull(action);

        // La stratégie de reprise (EnableRetryOnFailure) impose d'y placer toute transaction ouverte à la main.
        var strategy = _db.Database.CreateExecutionStrategy();
        var attempt = 0;
        return strategy.ExecuteAsync(async ct =>
        {
            // Une reprise rejoue l'action entière : les entités ajoutées par la tentative échouée ne doivent pas
            // être insérées une seconde fois.
            if (attempt++ > 0)
                _db.ChangeTracker.Clear();

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await AcquireAppLockAsync(resource, ct);
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    private async Task AcquireAppLockAsync(string resource, CancellationToken cancellationToken)
    {
        var status = new SqlParameter("@status", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        await _db.Database.ExecuteSqlRawAsync(
            "EXEC @status = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
            new object[] { status, new SqlParameter("@resource", resource), new SqlParameter("@timeout", ExclusiveLockTimeoutMs) },
            cancellationToken);
        // 0 ou 1 : verrou obtenu ; valeur négative : délai dépassé, annulation ou interblocage.
        if (status.Value is not int code || code < 0)
            throw new DomainException("Une opération identique est déjà en cours. Réessayez dans quelques secondes.");
    }

    private static bool IsLoginUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 } sql
           && sql.Message.Contains(LoginKey.UniqueIndexName, StringComparison.OrdinalIgnoreCase);
}
