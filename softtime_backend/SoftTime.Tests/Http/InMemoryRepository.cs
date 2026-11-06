using System.Linq.Expressions;

using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Tests.Http;

public sealed class InMemoryRepository<T> : IRepository<T> where T : class
{
    public List<T> Items { get; } = new();

    public IQueryable<T> Query() => Items.AsQueryable();

    public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        var d = Convert.ToDecimal(id);
        var match = Items.FirstOrDefault(i => i switch
        {
            Responsable r => r.IdResponsable == d,
            Role ro => ro.IdRole == d,
            BddSage s => s.Id == d,
            BddPointeuse p => p.Id == d,
            _ => false
        });
        return Task.FromResult(match);
    }

    public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Where(predicate.Compile()).ToList());

    public Task<List<T>> ListLatestAsync(Expression<Func<T, bool>> predicate, Expression<Func<T, DateTime?>> orderByDescending, int take, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Where(predicate.Compile()).OrderByDescending(orderByDescending.Compile()).Take(take).ToList());

    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        if (entity is Responsable r && r.IdResponsable == 0)
            r.IdResponsable = Items.OfType<Responsable>().Select(x => x.IdResponsable).DefaultIfEmpty(0).Max() + 1;
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        Items.AddRange(entities);
        return Task.CompletedTask;
    }

    public void Update(T entity) { }
    public void Remove(T entity) => Items.Remove(entity);
    public void RemoveRange(IEnumerable<T> entities)
    {
        foreach (var e in entities.ToList()) Items.Remove(e);
    }
}
