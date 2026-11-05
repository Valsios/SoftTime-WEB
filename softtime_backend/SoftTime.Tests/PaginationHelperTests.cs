using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using Xunit;

namespace SoftTime.Tests;

public class PaginationHelperTests
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
    public void CalculerPagination_NormaliseCorrectement(int? page, int? pageSize, int attenduPage, int attenduTaille)
    {
        var (p, s) = PaginationHelper.CalculerPagination(page, pageSize);
        Assert.Equal(attenduPage, p);
        Assert.Equal(attenduTaille, s);
    }

    [Fact]
    public async Task ToPagedAsync_PremierePage_RetournePremiereTrancheEtTotalExact()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Id),
            page: 1,
            pageSize: 10);

        Assert.Equal(25, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(1, result.Items[0].Id);
        Assert.Equal(10, result.Items[9].Id);
    }

    [Fact]
    public async Task ToPagedAsync_DernierePage_RetourneReliquatEtBonsTotaux()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Id),
            page: 3,
            pageSize: 10);

        Assert.Equal(25, result.Total);
        Assert.Equal(3, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(21, result.Items[0].Id);
        Assert.Equal(25, result.Items[4].Id);
    }

    [Fact]
    public async Task ToPagedAsync_PageHorsLimites_RetourneItemsVidesMaisTotalConserve()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Id),
            page: 10,
            pageSize: 10);

        Assert.Equal(25, result.Total);
        Assert.Equal(10, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ToPagedAsync_PageSizeTropGrand_PlafonneATailleMax()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 150; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D3}", i));

        var result = await repo.ToPagedAsync(
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Id),
            page: 1,
            pageSize: 500);

        Assert.Equal(100, result.PageSize);
        Assert.Equal(100, result.Items.Count);
        Assert.Equal(150, result.Total);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task ToPagedAsync_PageSizeInvalide_RetombeSurTailleDefaut()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 60; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i));

        var result = await repo.ToPagedAsync(
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Id),
            page: 1,
            pageSize: -1);

        Assert.Equal(PaginationRules.TailleDefaut, result.PageSize);
        Assert.Equal(50, result.Items.Count);
        Assert.Equal(60, result.Total);
    }

    [Fact]
    public async Task ToPagedAsync_ListeVide_RetourneZeroTotalEtUnePage()
    {
        var repo = new InMemoryTestRepository();

        var result = await repo.ToPagedAsync(
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Id),
            page: 1,
            pageSize: 10);

        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.TotalPages);
        Assert.Empty(result.Items);
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
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Category).ThenBy(x => x.Id),
            page: 1,
            pageSize: 10);

        Assert.Equal(new[] { 1, 2, 5, 8 }, result.Items.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task ToPagedAsync_CombinaisonFiltreEtPagination_FiltreEnAmont()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 30; i++)
            repo.Items.Add(new TestEntity(i, i % 2 == 0 ? "Pair" : "Impair", $"Item {i}", i));

        var result = await repo.ToPagedAsync(
            predicate: x => x.Category == "Pair",
            orderBy: q => q.OrderBy(x => x.Id),
            page: 2,
            pageSize: 5);

        // 15 pairs au total, page 2 de taille 5 -> éléments 6 à 10 (Ids: 12, 14, 16, 18, 20)
        Assert.Equal(15, result.Total);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(12, result.Items[0].Id);
        Assert.Equal(20, result.Items[4].Id);
    }

    [Fact]
    public async Task ToPagedAsync_AvecProjectionDto_MappeCorrectement()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 15; i++)
            repo.Items.Add(new TestEntity(i, "Cat", $"Nom {i}", i * 100));

        var result = await repo.ToPagedAsync(
            predicate: x => x.Id > 5,
            orderBy: q => q.OrderBy(x => x.Id),
            selector: x => new TestDto(x.Id, $"{x.Name} ({x.Value})"),
            page: 1,
            pageSize: 5);

        Assert.Equal(10, result.Total);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(6, result.Items[0].Id);
        Assert.Equal("Nom 6 (600)", result.Items[0].Label);
    }

    [Fact]
    public async Task ToPagedAsync_PageExtreme_ReturnsEmptyAndKeepsTotals()
    {
        var repo = new InMemoryTestRepository();
        for (int i = 1; i <= 25; i++)
            repo.Items.Add(new TestEntity(i, "CatA", $"Item {i:D2}", i * 10));

        var result = await repo.ToPagedAsync(
            predicate: null,
            orderBy: q => q.OrderBy(x => x.Id),
            page: int.MaxValue,
            pageSize: 10);

        Assert.Equal(25, result.Total);
        Assert.Equal(int.MaxValue, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ToPagedAsync_SansOrderBy_LanceArgumentNullException()
    {
        var repo = new InMemoryTestRepository();
        repo.Items.Add(new TestEntity(1, "Cat", "A", 10));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repo.ToPagedAsync(predicate: null, orderBy: null!, page: 1, pageSize: 10));
    }
}
