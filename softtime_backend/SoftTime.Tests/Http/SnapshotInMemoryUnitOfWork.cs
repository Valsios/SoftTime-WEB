using System.Text;
using System.Text.Json;

using SoftTime.Domain.Repositories;

namespace SoftTime.Tests.Http;

public sealed class SnapshotInMemoryUnitOfWork : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repos = new();
    private readonly object _gate = new();

    public InMemoryRepository<T> GetRepo<T>() where T : class
    {
        lock (_gate)
        {
            if (!_repos.TryGetValue(typeof(T), out var r))
                _repos[typeof(T)] = r = new InMemoryRepository<T>();
            return (InMemoryRepository<T>)r;
        }
    }

    public IRepository<T> Repository<T>() where T : class => GetRepo<T>();
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string Snapshot()
    {
        var options = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
        var sb = new StringBuilder();
        lock (_gate)
        {
            foreach (var kv in _repos.OrderBy(k => k.Key.Name).Where(k => ((System.Collections.ICollection)k.Value.GetType().GetProperty("Items")!.GetValue(k.Value)!).Count > 0))
            {
                var items = kv.Value.GetType().GetProperty("Items")!.GetValue(kv.Value)!;
                sb.Append(kv.Key.Name).Append('=').Append(JsonSerializer.Serialize(items, items.GetType(), options)).Append('\n');
            }
        }
        return sb.ToString();
    }
}
