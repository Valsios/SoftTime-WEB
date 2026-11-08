using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class PayrollCategoryMappingTests
{
    private static readonly HsExoDto Row = new(1, "MAT001", null, 1.5m, 2m, .5m, 1.25m, 3m, 4m, 2.5m, 0, 0);

    [Theory]
    [InlineData("Exo130", 1.5)]
    [InlineData("Exo150", 2)]
    [InlineData("I130", .5)]
    [InlineData("I150", 1.25)]
    [InlineData("Ferie", 3)]
    [InlineData("Dimanche", 2.5)]
    [InlineData("Nuit", 4)]
    public void MapsEachPayrollCategoryToItsOwnHsValue(string category, decimal expected)
    {
        // Act
        var result = OvertimePayrollCategoryMapper.ValueFor(category, Row);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void UnknownCategoryIsRejected()
    {
        // Act
        var act = () => OvertimePayrollCategoryMapper.ValueFor("Other", Row);

        // Assert
        act.Should().ThrowExactly<DomainException>();
    }

    [Fact]
    public void SqlIdentifiersRejectDangerousCharacters()
    {
        // Act
        var act = () => PayrollWritingConfiguration.ValidateIdentifier("PAIE;DROP TABLE", "table");

        // Assert
        act.Should().ThrowExactly<DomainException>();
    }

    [Fact]
    public void CompleteConfigurationIsAccepted()
        => PayrollWritingConfiguration.ValidateConfigurationRows(Configuration());

    [Fact]
    public void MissingCategoryIsRejected()
    {
        // Arrange
        var configuration = Configuration();
        configuration.RemoveAt(6);

        // Act
        var act = () => PayrollWritingConfiguration.ValidateConfigurationRows(configuration);

        // Assert
        act.Should().ThrowExactly<DomainException>();
    }

    [Fact]
    public void DuplicateCategoryIsRejected()
    {
        // Arrange
        var configuration = Configuration();
        configuration.Add(new PaieEcriture
        {
            BddSage = "TEST_AUTRE_RH", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye",
            Categorie = "Exo130", ColValeur = "HeuresExo130Bis"
        });

        // Act
        var act = () => PayrollWritingConfiguration.ValidateConfigurationRows(configuration);

        // Assert
        act.Should().ThrowExactly<DomainException>();
    }

    private static List<PaieEcriture> Configuration() => PayrollWritingConfiguration.Categories
        .Select(category => new PaieEcriture
        {
            BddSage = "TEST_AUTRE_RH",
            TableCible = "PAIE_CUMULS",
            ColMatricule = "MatriculeEmploye",
            Categorie = category,
            ColValeur = $"Heures{category}",
        }).ToList();
}
