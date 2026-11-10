using System.Linq.Expressions;

using SoftTime.Application.Services;
using SoftTime.Domain.Models;
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
        // Act
        var result = PagedLists.Page(Array.Empty<int>(), null, null);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(PaginationRules.TailleDefaut);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public void Page_PageBeyondLast_ClampsToLastPage()
    {
        // Act
        var result = PagedLists.Page(Enumerable.Range(1, 25).ToList(), page: 10, pageSize: 10);

        // Assert
        result.Page.Should().Be(3);
        result.TotalPages.Should().Be(3);
        result.Items.Count.Should().Be(5);
        result.Items[0].Should().Be(21);
        result.Items[^1].Should().Be(25);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(2, 2)]
    public void Page_InvalidPage_IsNormalized(int demandee, int attendue)
    {
        // Act
        var result = PagedLists.Page(Enumerable.Range(1, 100).ToList(), demandee, 10);

        // Assert
        result.Page.Should().Be(attendue);
    }

    [Fact]
    public void Page_PageSizeAboveMaximum_IsCapped()
    {
        // Act
        var result = PagedLists.Page(Enumerable.Range(1, 200).ToList(), page: 1, pageSize: 5000);

        // Assert
        result.PageSize.Should().Be(PaginationRules.TailleMax);
        result.Items.Count.Should().Be(PaginationRules.TailleMax);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public void Page_NoPageSize_UsesDefault()
    {
        // Act
        var result = PagedLists.Page(Enumerable.Range(1, 120).ToList(), page: null, pageSize: null);

        // Assert
        result.PageSize.Should().Be(PaginationRules.TailleDefaut);
        result.TotalPages.Should().Be(3);
        result.Items.Count.Should().Be(50);
    }

    [Fact]
    public void Page_OrderedSource_PreservesOrder()
    {
        // Arrange
        var source = new[] { "c", "a", "b" };

        // Act
        var result = PagedLists.Page(source, 1, 10);

        // Assert
        result.Items.Should().Equal(source);
    }

    [Fact]
    public async Task PageAsync_RepositorySource_CountsAndPages()
    {
        // Arrange
        var repository = new FakeRepository<Item>(
            Enumerable.Range(1, 120).Select(i => new Item(i)).ToList());

        // Act
        var result = await PagedLists.From(repository, _ => true, q => q.OrderBy(x => x.Value)).PageAsync(new PageRequest(2, 50), x => x.Value * 10);

        // Assert
        result.Total.Should().Be(120);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(50);
        result.TotalPages.Should().Be(3);
        result.Items.Count.Should().Be(50);
        result.Items[0].Should().Be(510);
        result.Items[^1].Should().Be(1000);
    }

    [Fact]
    public async Task PageAsync_EmptySource_DoesNotReadItems()
    {
        // Arrange
        var repository = new FakeRepository<Item>(Array.Empty<Item>());

        // Act
        var result = await PagedLists.From(repository, _ => false, q => q.OrderBy(x => x.Value)).PageAsync(new PageRequest(3, 50), x => x.Value);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        result.Page.Should().Be(1);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task PageAsync_PredicateAndPageBeyondLast_FiltersAndClamps()
    {
        // Arrange
        var repository = new FakeRepository<Item>(
            Enumerable.Range(1, 25).Select(i => new Item(i)).ToList());

        // Act
        var result = await PagedLists.From(repository, x => x.Value % 2 == 0, q => q.OrderBy(x => x.Value)).PageAsync(new PageRequest(99, 10), x => x.Value);

        // Assert
        result.Total.Should().Be(12);
        result.Page.Should().Be(2);
        result.TotalPages.Should().Be(2);
        result.Items.Should().Equal(new[] { 22, 24 });
    }

    [Fact]
    public void Page_PageExtreme_ClampsToLastPage()
    {
        // Act
        var result = PagedLists.Page(Enumerable.Range(1, 25).ToList(), page: int.MaxValue, pageSize: 10);

        // Assert
        result.Page.Should().Be(3);
        result.TotalPages.Should().Be(3);
        result.Items.Count.Should().Be(5);
        result.Items[0].Should().Be(21);
        result.Items[^1].Should().Be(25);
    }

    [Fact]
    public async Task PageAsync_PageExtreme_ClampsToLastPage()
    {
        // Arrange
        var repository = new FakeRepository<Item>(
            Enumerable.Range(1, 25).Select(i => new Item(i)).ToList());

        // Act
        var result = await PagedLists.From(repository, _ => true, q => q.OrderBy(x => x.Value)).PageAsync(new PageRequest(int.MaxValue, 10), x => x.Value);

        // Assert
        result.Total.Should().Be(25);
        result.Page.Should().Be(3);
        result.TotalPages.Should().Be(3);
        result.Items.Should().Equal(new[] { 21, 22, 23, 24, 25 });
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
