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
        // Act
        var result = SearchExpression.For<Row>("  ", row => row.Nom);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void BuildsOrAcrossFieldsAndAndAcrossWords()
    {
        // Arrange
        var rows = new[]
        {
            new Row { Nom = "Dupont René", Code = "MAT-01" },
            new Row { Nom = "Martin", Code = "MAT-02" },
            new Row { Nom = "Dupont", Code = "MAT-03" },
        }.AsQueryable();

        // Act
        var predicate = SearchExpression.For<Row>("dupont ren", row => row.Nom, row => row.Code);

        // Assert
        predicate.Should().NotBeNull();
        var result = rows.Where(predicate!).ToList();
        result.Should().ContainSingle();
        result[0].Code.Should().Be("MAT-01");
    }

    [Fact]
    public void NullFieldDoesNotMatch()
    {
        // Arrange
        var rows = new[] { new Row { Nom = null, Code = "X" } }.AsQueryable();

        // Act
        var predicate = SearchExpression.For<Row>("x", row => row.Nom);

        // Assert
        predicate.Should().NotBeNull();
        rows.Where(predicate!).ToList().Should().BeEmpty();
    }

    [Fact]
    public void AndAlsoCombinesPredicates()
    {
        // Arrange
        var rows = new[]
        {
            new Row { Nom = "Dupont", Code = "A" },
            new Row { Nom = "Dupont", Code = "B" },
            new Row { Nom = "Martin", Code = "A" },
        }.AsQueryable();
        var left = SearchExpression.For<Row>("dupont", row => row.Nom)!;
        var right = SearchExpression.For<Row>("a", row => row.Code)!;
        var combined = SearchExpression.AndAlso(left, right);

        // Act
        var result = rows.Where(combined).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Code.Should().Be("A");
    }
}
