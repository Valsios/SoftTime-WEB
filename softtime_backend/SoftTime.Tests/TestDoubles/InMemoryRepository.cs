using System.Linq.Expressions;

using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Tests;

/// <summary>
/// Dépôt en mémoire partagé par les tests. Les entités à clé connue (<see cref="EntityKeys"/>) sont
/// recherchées par clé et numérotées « max + 1 » ; les autres reçoivent un identifiant séquentiel.
/// </summary>
public sealed class InMemoryRepository<T> : IRepository<T> where T : class
{
    private decimal _nextId = 1;

    public List<T> Items { get; } = new();

    public IQueryable<T> Query() => Items.AsQueryable();

    public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        if (!EntityKeys.IsKeyed(typeof(T)))
            return Task.FromResult(Items.FirstOrDefault());
        var key = Convert.ToDecimal(id);
        return Task.FromResult(Items.FirstOrDefault(item => EntityKeys.Of(item) == key));
    }

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

    /// <summary>Numérote une entité ajoutée sans identifiant.</summary>
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
            default:
                AssignNextKey(entity);
                break;
        }
    }

    /// <summary>Clé « max + 1 » des entités à clé connue ajoutées sans clé.</summary>
    private void AssignNextKey(T entity)
    {
        if (EntityKeys.Of(entity) != 0)
            return;
        var next = Items.Select(item => EntityKeys.Of(item) ?? 0).DefaultIfEmpty(0).Max() + 1;
        switch (entity)
        {
            case Responsable responsable:
                responsable.IdResponsable = next;
                break;
            case Role role:
                role.IdRole = next;
                break;
            case RolePermission permission:
                permission.Id = (int)next;
                break;
            case AuditDroit audit:
                audit.Id = (int)next;
                break;
            case UserPermission permission:
                permission.Id = (int)next;
                break;
            case SuiviConfiguration configuration:
                configuration.Id = (int)next;
                break;
        }
    }
}
