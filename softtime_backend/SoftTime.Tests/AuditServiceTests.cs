using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using Xunit;

namespace SoftTime.Tests;

public class AuditServiceTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _uow = new InMemoryUnitOfWork();
        _service = new AuditService(_uow);
    }

    [Fact]
    public void ListAllowedTypes_ReturnsNonEmptyOptions()
    {
        var types = _service.ListAllowedTypes();
        Assert.NotEmpty(types);
        Assert.Contains(types, t => t.Value == "nvarchar");
    }

    [Fact]
    public async Task SaveConfigAsync_Create_WithValidType_NormalizesAndAddsEntity()
    {
        var dto = new AuditConfigDto(0, "T_EMPLOYEE", "NOM", "  VARCHAR  ", true, "SOFTTIME_SUITE");

        await _service.SaveConfigAsync(dto);

        var repo = _uow.GetRepo<suivi_TBLCLN>();
        Assert.Single(repo.Items);
        var added = repo.Items[0];
        Assert.Equal("T_EMPLOYEE", added.TABLES);
        Assert.Equal("NOM", added.COLONNES);
        Assert.Equal("varchar", added.TYPES);
        Assert.True(added.ETATS);
        Assert.Equal("SOFTTIME_SUITE", added.BASES);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("text")]
    [InlineData("image")]
    [InlineData("not_a_sql_type")]
    public async Task SaveConfigAsync_Create_WithInvalidType_ThrowsInvalidOperationException(string? invalidType)
    {
        var dto = new AuditConfigDto(0, "T_EMPLOYEE", "NOM", invalidType, true, "SOFTTIME_SUITE");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveConfigAsync(dto));

        var repo = _uow.GetRepo<suivi_TBLCLN>();
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WithUnchangedLegacyType_DoesNotThrowAndPreservesValue()
    {
        var repo = _uow.GetRepo<suivi_TBLCLN>();
        var existing = new suivi_TBLCLN
        {
            Id = 42,
            TABLES = "T_LOG",
            COLONNES = "COMMENTAIRE",
            TYPES = "legacy_exotic_type",
            ETATS = true,
            BASES = "SOFTTIME_SUITE"
        };
        repo.Items.Add(existing);

        var dto = new AuditConfigDto(42, "T_LOG", "COMMENTAIRE_RENOMME", "legacy_exotic_type", false, "SOFTTIME_SUITE");

        await _service.SaveConfigAsync(dto);

        Assert.Equal("COMMENTAIRE_RENOMME", existing.COLONNES);
        Assert.Equal("legacy_exotic_type", existing.TYPES);
        Assert.False(existing.ETATS);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WithModifiedValidType_ValidatesAndNormalizes()
    {
        var repo = _uow.GetRepo<suivi_TBLCLN>();
        var existing = new suivi_TBLCLN
        {
            Id = 10,
            TABLES = "T_TEST",
            COLONNES = "COL1",
            TYPES = "varchar",
            ETATS = true,
            BASES = "SOFTTIME_SUITE"
        };
        repo.Items.Add(existing);

        var dto = new AuditConfigDto(10, "T_TEST", "COL1", "  INT  ", true, "SOFTTIME_SUITE");

        await _service.SaveConfigAsync(dto);

        Assert.Equal("int", existing.TYPES);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WithModifiedInvalidType_ThrowsInvalidOperationExceptionAndLeavesOriginal()
    {
        var repo = _uow.GetRepo<suivi_TBLCLN>();
        var existing = new suivi_TBLCLN
        {
            Id = 10,
            TABLES = "T_TEST",
            COLONNES = "COL1",
            TYPES = "varchar",
            ETATS = true,
            BASES = "SOFTTIME_SUITE"
        };
        repo.Items.Add(existing);

        var dto = new AuditConfigDto(10, "T_TEST", "COL1", "invalid_type", true, "SOFTTIME_SUITE");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveConfigAsync(dto));

        Assert.Equal("varchar", existing.TYPES);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WhenNotFound_ThrowsKeyNotFoundException()
    {
        var dto = new AuditConfigDto(999, "T_TEST", "COL1", "int", true, "SOFTTIME_SUITE");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SaveConfigAsync(dto));
    }

    #region In-Memory Test Doubles

    private sealed class InMemoryRepository<T> : IRepository<T> where T : class
    {
        public List<T> Items { get; } = new();

        public IQueryable<T> Query() => Items.AsQueryable();

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            var intId = Convert.ToInt32(id);
            var match = Items.FirstOrDefault(i =>
            {
                if (i is suivi_TBLCLN c && c.Id == intId) return true;
                return false;
            });
            return Task.FromResult(match);
        }

        public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            return Task.FromResult(Items.Where(func).ToList());
        }

        public Task<List<T>> ListLatestAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<T, DateTime?>> orderByDescending,
            int take,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            var orderFunc = orderByDescending.Compile();
            return Task.FromResult(Items.Where(func).OrderByDescending(orderFunc).Take(take).ToList());
        }

        public Task<List<T>> ListPagedAsync(
            Expression<Func<T, bool>> predicate,
            Func<IQueryable<T>, IOrderedQueryable<T>> orderBy,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            var items = orderBy(Items.Where(func).AsQueryable()).Skip(skip).Take(take).ToList();
            return Task.FromResult(items);
        }

        public Task<int> CountAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            return Task.FromResult(Items.Count(func));
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is suivi_TBLCLN c && c.Id == 0)
            {
                c.Id = Items.Count == 0 ? 1 : Items.OfType<suivi_TBLCLN>().Max(x => x.Id) + 1;
            }
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            foreach (var e in entities)
                AddAsync(e, cancellationToken);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Remove(T entity) => Items.Remove(entity);
        public void RemoveRange(IEnumerable<T> entities)
        {
            foreach (var e in entities.ToList())
                Items.Remove(e);
        }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repos = new();

        public InMemoryRepository<T> GetRepo<T>() where T : class
        {
            if (!_repos.TryGetValue(typeof(T), out var r))
            {
                r = new InMemoryRepository<T>();
                _repos[typeof(T)] = r;
            }
            return (InMemoryRepository<T>)r;
        }

        public IRepository<T> Repository<T>() where T : class => GetRepo<T>();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    #endregion
}
