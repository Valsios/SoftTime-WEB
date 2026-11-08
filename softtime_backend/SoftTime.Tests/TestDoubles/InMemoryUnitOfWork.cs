using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using SoftTime.Domain.Repositories;

namespace SoftTime.Tests;

/// <summary>Unité de travail en mémoire : un <see cref="InMemoryRepository{T}"/> par type d'entité.</summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repos = new();
    private readonly object _gate = new();

    /// <summary>Nombre d'appels à <see cref="SaveChangesAsync"/>.</summary>
    public int SaveChangesCount { get; private set; }

    public void ResetSaveChangesCount() => SaveChangesCount = 0;

    /// <summary>Vide tous les dépôts.</summary>
    public void Clear()
    {
        lock (_gate)
            _repos.Clear();
    }

    public InMemoryRepository<T> Get<T>() where T : class
    {
        lock (_gate)
        {
            if (!_repos.TryGetValue(typeof(T), out var repo))
                _repos[typeof(T)] = repo = new InMemoryRepository<T>();
            return (InMemoryRepository<T>)repo;
        }
    }

    public IRepository<T> Repository<T>() where T : class => Get<T>();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.FromResult(1);
    }

    public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Contenu sérialisé des dépôts non vides, trié par type : compare l'état avant et après un appel.</summary>
    public string Snapshot()
    {
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles };
        var sb = new StringBuilder();
        lock (_gate)
        {
            foreach (var (type, repo) in _repos.OrderBy(pair => pair.Key.Name))
            {
                var items = (ICollection)repo.GetType().GetProperty("Items")!.GetValue(repo)!;
                if (items.Count == 0)
                    continue;
                sb.Append(type.Name).Append('=').Append(JsonSerializer.Serialize(items, items.GetType(), options)).Append('\n');
            }
        }
        return sb.ToString();
    }
}
