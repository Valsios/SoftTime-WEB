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
        => Assert.Equal(expected, OvertimePayrollCategoryMapper.ValueFor(category, Row));

    [Fact]
    public void UnknownCategoryIsRejected()
        => Assert.Throws<DomainException>(() => OvertimePayrollCategoryMapper.ValueFor("Other", Row));

    [Fact]
    public void SqlIdentifiersRejectDangerousCharacters()
        => Assert.Throws<DomainException>(() => PayrollWritingConfiguration.ValidateIdentifier("PAIE;DROP TABLE", "table"));

    [Fact]
    public void CompleteConfigurationIsAccepted()
        => PayrollWritingConfiguration.ValidateConfigurationRows(Configuration());

    [Fact]
    public void MissingCategoryIsRejected()
    {
        var configuration = Configuration();
        configuration.RemoveAt(6);
        Assert.Throws<DomainException>(() => PayrollWritingConfiguration.ValidateConfigurationRows(configuration));
    }

    [Fact]
    public void DuplicateCategoryIsRejected()
    {
        var configuration = Configuration();
        configuration.Add(new PaieEcriture
        {
            BddSage = "TEST_AUTRE_RH", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye",
            Categorie = "Exo130", ColValeur = "HeuresExo130Bis"
        });
        Assert.Throws<DomainException>(() => PayrollWritingConfiguration.ValidateConfigurationRows(configuration));
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
