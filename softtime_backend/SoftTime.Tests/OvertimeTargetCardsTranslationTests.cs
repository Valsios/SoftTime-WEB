using Microsoft.EntityFrameworkCore;

using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Services.Overtime;

namespace SoftTime.Tests;

/// <summary>
/// Filtre SQL des cartes de la page Heures supplémentaires (<see cref="OvertimeWeekContext.CartesDuPerimetre"/>).
/// Le fournisseur InMemory accepte n'importe quelle expression : il ne prouve donc rien sur la traduction. Ces tests
/// utilisent le fournisseur SQL Server avec <c>ToQueryString()</c> (aucune connexion ouverte) : une expression que EF Core
/// ne sait pas traduire, comme <c>string.Equals(a, b, StringComparison.OrdinalIgnoreCase)</c>, lève une
/// <see cref="InvalidOperationException"/> (500 sur <c>api/v1/overtime/paged?departement=…</c>).
/// </summary>
public class OvertimeTargetCardsTranslationTests
{
    private static readonly DateTime From = new(2026, 9, 14);
    private static readonly DateTime To = new(2026, 10, 11);

    [Fact]
    public void CartesDuPerimetre_WithDepartment_IsTranslatedToSqlServer()
    {
        // Arrange
        using var db = CreateContext();
        var predicate = OvertimeWeekContext.CartesDuPerimetre("SAGE_TEST", new PeriodRequest(null, null, From, To, "BR704"));

        // Act
        var sql = db.Set<CardPaie>().Where(predicate).ToQueryString();

        // Assert
        sql.Should().Contain("UPPER(").And.Contain("[DEPARTEMENT]").And.Contain("[SAGE_BDD]");
        sql.Should().Contain("N'BR704'");
    }

    [Fact]
    public void CartesDuPerimetre_DepartmentInLowerCase_ComparesTheUpperCaseValue()
    {
        // Arrange
        using var db = CreateContext();
        var predicate = OvertimeWeekContext.CartesDuPerimetre("SAGE_TEST", new PeriodRequest(null, null, From, To, "br704"));

        // Act
        var sql = db.Set<CardPaie>().Where(predicate).ToQueryString();

        // Assert : casse du paramètre ramenée à la casse de UPPER(colonne), quelle que soit la collation de la colonne.
        sql.Should().Contain("N'BR704'").And.NotContain("N'br704'");
    }

    [Fact]
    public void CartesDuPerimetre_FullPerimeter_IsTranslatedWithMatriculeRange()
    {
        // Arrange
        using var db = CreateContext();
        var predicate = OvertimeWeekContext.CartesDuPerimetre("SAGE_TEST", new PeriodRequest("00100", "00900", From, To, "BR704"));

        // Act
        var act = () => db.Set<CardPaie>().Where(predicate).ToQueryString();

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CartesDuPerimetre_WithoutDepartment_AddsNoDepartmentComparison(string? departement)
    {
        // Arrange
        using var db = CreateContext();
        var predicate = OvertimeWeekContext.CartesDuPerimetre("SAGE_TEST", new PeriodRequest(null, null, From, To, departement));

        // Act
        var sql = db.Set<CardPaie>().Where(predicate).ToQueryString();

        // Assert
        sql.Should().NotContain("UPPER(");
    }

    [Theory]
    [InlineData("BR704", true)]
    [InlineData("br704", true)]
    [InlineData("Br704", true)]
    [InlineData("BR730", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void CartesDuPerimetre_Semantics_DepartmentIsCaseInsensitive(string? demande, bool attendu)
    {
        // Arrange : mêmes cartes, quelle que soit la casse stockée du département.
        var cartes = new[]
        {
            new CardPaie { Id = 1, SageBdd = "SAGE_TEST", SageMatricule = "00100", Departement = "BR704" },
            new CardPaie { Id = 2, SageBdd = "SAGE_TEST", SageMatricule = "00200", Departement = "br704" },
            new CardPaie { Id = 3, SageBdd = "AUTRE_BASE", SageMatricule = "00300", Departement = "BR704" },
            new CardPaie { Id = 4, SageBdd = "SAGE_TEST", SageMatricule = "00400", Departement = null },
        };
        var predicate = OvertimeWeekContext.CartesDuPerimetre("SAGE_TEST", new PeriodRequest(null, null, From, To, demande)).Compile();

        // Act
        var retenues = cartes.Where(predicate).Select(c => c.Id).ToArray();

        // Assert
        retenues.Contains(1).Should().Be(attendu);
        retenues.Contains(2).Should().Be(attendu);
        retenues.Should().NotContain(3, "une autre base SAGE n'entre jamais dans le périmètre");
        retenues.Contains(4).Should().Be(string.IsNullOrEmpty(demande), "un département absent ne correspond à aucun département demandé");
    }

    [Fact]
    public void CartesDuPerimetre_MatriculeRange_KeepsTheHistoricalBounds()
    {
        // Arrange
        var cartes = new[]
        {
            new CardPaie { Id = 1, SageBdd = "SAGE_TEST", SageMatricule = "00100" },
            new CardPaie { Id = 2, SageBdd = "SAGE_TEST", SageMatricule = "00500" },
            new CardPaie { Id = 3, SageBdd = "SAGE_TEST", SageMatricule = "00900" },
        };
        var predicate = OvertimeWeekContext.CartesDuPerimetre("SAGE_TEST", new PeriodRequest("00200", "00900", From, To)).Compile();

        // Act
        var retenues = cartes.Where(predicate).Select(c => c.Id).ToArray();

        // Assert
        retenues.Should().Equal(2, 3);
    }

    private static SoftTimeDbContext CreateContext() => new(new DbContextOptionsBuilder<SoftTimeDbContext>()
        .UseSqlServer("Server=.;Database=translation", sql => sql.UseCompatibilityLevel(120))
        .Options);
}
