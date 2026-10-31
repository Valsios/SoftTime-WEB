using SoftTime.Application.Services;

namespace SoftTime.Tests;

public class SearchExpressionTests
{
    private sealed class Row
    {
        public string? Nom { get; init; }
        public string? Code { get; init; }
    }

    [Fact]
    public void EmptySearchReturnsNull()
    {
        Assert.Null(SearchExpression.For<Row>("  ", row => row.Nom));
    }

    [Fact]
    public void BuildsOrAcrossFieldsAndAndAcrossWords()
    {
        var rows = new[]
        {
            new Row { Nom = "Dupont René", Code = "MAT-01" },
            new Row { Nom = "Martin", Code = "MAT-02" },
            new Row { Nom = "Dupont", Code = "MAT-03" },
        }.AsQueryable();

        var predicate = SearchExpression.For<Row>("dupont ren", row => row.Nom, row => row.Code);

        Assert.NotNull(predicate);
        var result = rows.Where(predicate!).ToList();
        Assert.Single(result);
        Assert.Equal("MAT-01", result[0].Code);
    }

    [Fact]
    public void NullFieldDoesNotMatch()
    {
        var rows = new[] { new Row { Nom = null, Code = "X" } }.AsQueryable();
        var predicate = SearchExpression.For<Row>("x", row => row.Nom);

        Assert.NotNull(predicate);
        Assert.Empty(rows.Where(predicate!).ToList());
    }

    [Fact]
    public void AndAlsoCombinesPredicates()
    {
        var rows = new[]
        {
            new Row { Nom = "Dupont", Code = "A" },
            new Row { Nom = "Dupont", Code = "B" },
            new Row { Nom = "Martin", Code = "A" },
        }.AsQueryable();

        var left = SearchExpression.For<Row>("dupont", row => row.Nom)!;
        var right = SearchExpression.For<Row>("a", row => row.Code)!;
        var combined = SearchExpression.AndAlso(left, right);

        var result = rows.Where(combined).ToList();
        Assert.Single(result);
        Assert.Equal("A", result[0].Code);
    }
}
