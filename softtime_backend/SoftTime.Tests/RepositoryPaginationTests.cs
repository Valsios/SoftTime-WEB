using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class RepositoryPaginationTests
{
    private record TestEntity(int Id, string Category, string Name, int Value);
    private record TestDto(int Id, string Label);

    private class InMemoryTestRepository : IRepository<TestEntity>
    {
        public List<TestEntity> Items { get; } = new();

        public IQueryable<TestEntity> Query() => Items.AsQueryable();

        public Task<TestEntity?> GetByIdAsync(object id, System.Threading.CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == Convert.ToInt32(id)));

        public Task<List<TestEntity>> ListAsync(System.Linq.Expressions.Expression<Func<TestEntity, bool>> predicate, System.Threading.CancellationToken ct = default)
            => Task.FromResult(Items.Where(predicate.Compile()).ToList());

        public Task<List<TestEntity>> ListLatestAsync(
            System.Linq.Expressions.Expression<Func<TestEntity, bool>> predicate,
            System.Linq.Expressions.Expression<Func<TestEntity, DateTime?>> orderByDescending,
            int take,
            System.Threading.CancellationToken ct = default)
            => Task.FromResult(Items.Where(predicate.Compile()).Take(take).ToList());

        public Task AddAsync(TestEntity entity, System.Threading.CancellationToken ct = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<TestEntity> entities, System.Threading.CancellationToken ct = default)
        {
            Items.AddRange(entities);
            return Task.CompletedTask;
        }

        public void Update(TestEntity entity) { }
        public void Remove(TestEntity entity) => Items.Remove(entity);
        public void RemoveRange(IEnumerable<TestEntity> entities)
        {
            foreach (var e in entities.ToList()) Items.Remove(e);
        }
    }

    [Theory]
    [InlineData(1, 10, 1, 10)]
    [InlineData(null, null, 1, 50)]
    [InlineData(0, 0, 1, 50)]
    [InlineData(-5, -1, 1, 50)]
    [InlineData(2, 250, 2, 100)]
    [InlineData(3, 100, 3, 100)]
    [InlineData(4, 25, 4, 25)]
    public void Normaliser_PageEtTaille_SontBornees(int? page, int? pageSize, int attenduPage, int attenduTaille)
    {
        var (p, s) = PaginationRules.Normaliser(page, pageSize);
        p.Should().Be(attenduPage);
        s.Should().Be(attenduTaille);
    }

    [Fact]
    public async Task ToPagedAsync_PremierePage_RetournePremiereTrancheEtTotalExact()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Id),
            new PageRequest(1, 10));

        result.Total.Should().Be(25);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.Items.Count.Should().Be(10);
        result.Items[0].Id.Should().Be(1);
        result.Items[9].Id.Should().Be(10);
    }

    [Fact]
    public async Task ToPagedAsync_DernierePage_RetourneReliquatEtBonsTotaux()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Id),
            new PageRequest(3, 10));

        result.Total.Should().Be(25);
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.Items.Count.Should().Be(5);
        result.Items[0].Id.Should().Be(21);
        result.Items[4].Id.Should().Be(25);
    }

    [Fact]
    public async Task ToPagedAsync_PageHorsLimites_RetourneItemsVidesMaisTotalConserve()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Id),
            new PageRequest(10, 10));

        result.Total.Should().Be(25);
        result.Page.Should().Be(10);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ToPagedAsync_PageSizeTropGrand_PlafonneATailleMax()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 150; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D3}", i));

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Id),
            new PageRequest(1, 500));

        result.PageSize.Should().Be(100);
        result.Items.Count.Should().Be(100);
        result.Total.Should().Be(150);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task ToPagedAsync_PageSizeInvalide_RetombeSurTailleDefaut()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 60; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i));

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Id),
            new PageRequest(1, -1));

        result.PageSize.Should().Be(PaginationRules.TailleDefaut);
        result.Items.Count.Should().Be(50);
        result.Total.Should().Be(60);
    }

    [Fact]
    public async Task ToPagedAsync_ListeVide_RetourneZeroTotalEtUnePage()
    {
        var repo = new InMemoryTestRepository();

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Id),
            new PageRequest(1, 10));

        result.Total.Should().Be(0);
        result.TotalPages.Should().Be(1);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ToPagedAsync_TriStableAvecDoublons_GarantitOrdreDeterministe()
    {
        var repo = new InMemoryTestRepository();
        // Doublons sur Category, departages par Id
        repo.Items.Add(new TestEntity(5, "SameCat", "B", 20));
        repo.Items.Add(new TestEntity(2, "SameCat", "A", 10));
        repo.Items.Add(new TestEntity(8, "SameCat", "C", 30));
        repo.Items.Add(new TestEntity(1, "SameCat", "Z", 5));

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Category).ThenBy(x => x.Id),
            new PageRequest(1, 10));

        result.Items.Select(x => x.Id).ToArray().Should().Equal(new[] { 1, 2, 5, 8 });
    }

    [Fact]
    public async Task ToPagedAsync_CombinaisonFiltreEtPagination_FiltreEnAmont()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 30; i++)
            repo.Items.Add(new TestEntity(i, i % 2 == 0 ? "Pair" : "Impair", $"Item {i}", i));

        var result = await repo.ToPagedAsync(
            x => x.Category == "Pair",
            q => q.OrderBy(x => x.Id),
            new PageRequest(2, 5));

        // 15 pairs au total, page 2 de taille 5 -> éléments 6 à 10 (Ids: 12, 14, 16, 18, 20)
        result.Total.Should().Be(15);
        result.TotalPages.Should().Be(3);
        result.Items.Count.Should().Be(5);
        result.Items[0].Id.Should().Be(12);
        result.Items[4].Id.Should().Be(20);
    }

    [Fact]
    public async Task ToPagedAsync_AvecProjectionDto_MappeCorrectement()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 15; i++)
            repo.Items.Add(new TestEntity(i, "Cat", $"Nom {i}", i * 100));

        var result = await repo.ToPagedAsync(
            x => x.Id > 5,
            q => q.OrderBy(x => x.Id),
            x => new TestDto(x.Id, $"{x.Name} ({x.Value})"),
            new PageRequest(1, 5));

        result.Total.Should().Be(10);
        result.Items.Count.Should().Be(5);
        result.Items[0].Id.Should().Be(6);
        result.Items[0].Label.Should().Be("Nom 6 (600)");
    }

    [Fact]
    public async Task ToPagedAsync_PageExtreme_ReturnsEmptyAndKeepsTotals()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            null,
            q => q.OrderBy(x => x.Id),
            new PageRequest(int.MaxValue, 10));

        result.Total.Should().Be(25);
        result.Page.Should().Be(int.MaxValue);
        result.PageSize.Should().Be(10);
        result.TotalPages.Should().Be(3);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ToPagedAsync_SansOrderBy_LanceArgumentNullException()
    {
        var repo = new InMemoryTestRepository();
        repo.Items.Add(new TestEntity(1, "Cat", "A", 10));

        await FluentActions.Awaiting(() =>
            repo.ToPagedAsync(null, null!, new PageRequest(1, 10))).Should().ThrowExactlyAsync<ArgumentNullException>();
    }
}
