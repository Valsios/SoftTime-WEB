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
        => Assert.Throws<InvalidOperationException>(() => OvertimePayrollCategoryMapper.ValueFor("Other", Row));

    [Fact]
    public void SqlIdentifiersRejectDangerousCharacters()
        => Assert.Throws<InvalidOperationException>(() => PayrollWritingConfiguration.ValidateIdentifier("PAIE;DROP TABLE", "table"));

    [Fact]
    public void CompleteConfigurationIsAccepted()
        => PayrollWritingConfiguration.ValidateConfigurationRows(Configuration());

    [Fact]
    public void MissingCategoryIsRejected()
    {
        var configuration = Configuration();
        configuration.RemoveAt(6);
        Assert.Throws<InvalidOperationException>(() => PayrollWritingConfiguration.ValidateConfigurationRows(configuration));
    }

    [Fact]
    public void DuplicateCategoryIsRejected()
    {
        var configuration = Configuration();
        configuration.Add(new T_PAIE_ECRITURE
        {
            BDD_SAGE = "TEST_AUTRE_RH", TABLE_CIBLE = "PAIE_CUMULS", COL_MATRICULE = "MatriculeEmploye",
            CATEGORIE = "Exo130", COL_VALEUR = "HeuresExo130Bis"
        });
        Assert.Throws<InvalidOperationException>(() => PayrollWritingConfiguration.ValidateConfigurationRows(configuration));
    }

    private static List<T_PAIE_ECRITURE> Configuration() => PayrollWritingConfiguration.Categories
        .Select(category => new T_PAIE_ECRITURE
        {
            BDD_SAGE = "TEST_AUTRE_RH",
            TABLE_CIBLE = "PAIE_CUMULS",
            COL_MATRICULE = "MatriculeEmploye",
            CATEGORIE = category,
            COL_VALEUR = $"Heures{category}",
        }).ToList();
}
