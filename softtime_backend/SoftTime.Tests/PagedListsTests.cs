using System.Linq.Expressions;
using SoftTime.Application.Services;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Pagination partagée : bornage de la page et de la taille, cohérence <c>Total</c>/<c>Items</c>,
/// et découpage après agrégation pour les listes calculées.
/// </summary>
public class PagedListsTests
{
    [Fact]
    public void Page_EmptySource_ReturnsOnePage()
    {
        var result = PagedLists.Page(Array.Empty<int>(), null, null);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(PaginationRules.TailleDefaut, result.PageSize);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public void Page_ClampsPageToLastPage()
    {
        var result = PagedLists.Page(Enumerable.Range(1, 25).ToList(), page: 10, pageSize: 10);

        Assert.Equal(3, result.Page);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(21, result.Items[0]);
        Assert.Equal(25, result.Items[^1]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(2, 2)]
    public void Page_NormalizesPage(int demandee, int attendue)
    {
        var result = PagedLists.Page(Enumerable.Range(1, 100).ToList(), demandee, 10);

        Assert.Equal(attendue, result.Page);
    }

    [Fact]
    public void Page_CapsPageSizeAtMaximum()
    {
        var result = PagedLists.Page(Enumerable.Range(1, 200).ToList(), page: 1, pageSize: 5000);

        Assert.Equal(PaginationRules.TailleMax, result.PageSize);
        Assert.Equal(PaginationRules.TailleMax, result.Items.Count);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void Page_FallsBackOnDefaultPageSize()
    {
        var result = PagedLists.Page(Enumerable.Range(1, 120).ToList(), page: null, pageSize: null);

        Assert.Equal(PaginationRules.TailleDefaut, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(50, result.Items.Count);
    }

    [Fact]
    public void Page_PreservesOrder()
    {
        var source = new[] { "c", "a", "b" };

        var result = PagedLists.Page(source, 1, 10);

        Assert.Equal(source, result.Items);
    }

    [Fact]
    public async Task PageAsync_CountsAndPages()
    {
        var repository = new FakeRepository<Item>(
            Enumerable.Range(1, 120).Select(i => new Item(i)).ToList());

        var result = await PagedLists.PageAsync(
            repository,
            _ => true,
            q => q.OrderBy(x => x.Value),
            page: 2,
            pageSize: 50,
            map: x => x.Value * 10);

        Assert.Equal(120, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(50, result.Items.Count);
        Assert.Equal(510, result.Items[0]);
        Assert.Equal(1000, result.Items[^1]);
    }

    [Fact]
    public async Task PageAsync_EmptySource_DoesNotReadItems()
    {
        var repository = new FakeRepository<Item>(Array.Empty<Item>());

        var result = await PagedLists.PageAsync(
            repository,
            _ => false,
            q => q.OrderBy(x => x.Value),
            page: 3,
            pageSize: 50,
            map: x => x.Value);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task PageAsync_AppliesPredicateAndClampsPage()
    {
        var repository = new FakeRepository<Item>(
            Enumerable.Range(1, 25).Select(i => new Item(i)).ToList());

        var result = await PagedLists.PageAsync(
            repository,
            x => x.Value % 2 == 0,
            q => q.OrderBy(x => x.Value),
            page: 99,
            pageSize: 10,
            map: x => x.Value);

        Assert.Equal(12, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(new[] { 22, 24 }, result.Items);
    }

    [Fact]
    public void Page_PageExtreme_ClampsToLastPage()
    {
        var result = PagedLists.Page(Enumerable.Range(1, 25).ToList(), page: int.MaxValue, pageSize: 10);

        Assert.Equal(3, result.Page);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(21, result.Items[0]);
        Assert.Equal(25, result.Items[^1]);
    }

    [Fact]
    public async Task PageAsync_PageExtreme_ClampsToLastPage()
    {
        var repository = new FakeRepository<Item>(
            Enumerable.Range(1, 25).Select(i => new Item(i)).ToList());

        var result = await PagedLists.PageAsync(
            repository,
            _ => true,
            q => q.OrderBy(x => x.Value),
            page: int.MaxValue,
            pageSize: 10,
            map: x => x.Value);

        Assert.Equal(25, result.Total);
        Assert.Equal(3, result.Page);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(new[] { 21, 22, 23, 24, 25 }, result.Items);
    }

    private sealed record Item(int Value);

    /// <summary>Dépôt en mémoire : vérifie la logique de pagination sans dépendre d'EF.</summary>
    private sealed class FakeRepository<T> : IRepository<T> where T : class
    {
        private readonly List<T> _items;

        public FakeRepository(IEnumerable<T> items) => _items = items.ToList();

        public IQueryable<T> Query() => _items.AsQueryable();

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(predicate).ToList());

        public Task<List<T>> ListLatestAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<T, DateTime?>> orderByDescending,
            int take,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(T entity) => throw new NotSupportedException();

        public void Remove(T entity) => throw new NotSupportedException();

        public void RemoveRange(IEnumerable<T> entities) => throw new NotSupportedException();
    }
}
