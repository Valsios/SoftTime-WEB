using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

/// <summary>
/// Tests de caractérisation de DeduirePortes et BuildDefaultPermissionsList : ils figent le résultat d'AVANT l'extraction
/// (écrits et exécutés contre les méthodes privées d'AuthService), puis prouvent que le helper partagé donne le même résultat.
/// </summary>
public class PermissionCatalogCharacterizationTests
{
    // Les attentes ci-dessous ont été écrites et exécutées contre les méthodes privées d'AuthService AVANT l'extraction ;
    // elles s'exécutent maintenant contre le helper partagé (mêmes valeurs attendues).
    private static HashSet<decimal> Gates(IEnumerable<string> codes) => PermissionCatalog.DeduirePortes(codes);

    private static List<string> Defaults(HashSet<int> rights, IEnumerable<BddSage> sages, IEnumerable<BddPointeuse> pointeuses)
        => PermissionCatalog.BuildDefaultPermissionsList(rights, sages, pointeuses);

    [Fact]
    public void KnownStatic_Catalog_IsTheCodesOfEveryScreen()
    {
        // Arrange
        var screens = PermissionCatalog.Categories.SelectMany(c => c.Items).SelectMany(i => i.Codes()).ToList();

        // Act
        var result = PermissionCatalog.KnownStatic.SetEquals(screens);

        // Assert
        result.Should().BeTrue();
        screens.Should().OnlyHaveUniqueItems();
        PermissionCatalog.KnownStatic.Should().Contain(PermissionCatalog.SelfRead);
    }

    [Theory]
    [InlineData("SAGE:X:READ", true)]
    [InlineData("pointeuse:PTG:delete", true)]
    [InlineData("SAGE:CREATE", true)]
    [InlineData("proc:shifts", true)]
    [InlineData("SELF:READ", true)]
    [InlineData("SAGE:X:WRITE", false)]
    [InlineData("SAGE::READ", false)]
    [InlineData("SAGE:A:B:READ", false)]
    [InlineData("SAGE", false)]
    [InlineData("N'IMPORTE:QUOI", false)]
    [InlineData("", false)]
    public void IsKnown_Code_AcceptsCatalogCodesAndWellFormedDatabaseCodesOnly(string code, bool expected)
    {
        // Act
        var result = PermissionCatalog.IsKnown(code);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void DeduirePortes_SelfRead_OpensNoGate()
    {
        // Act
        var result = Gates(new[] { PermissionCatalog.SelfRead });

        // Assert
        result.Should().BeEmpty();
    }

    private static readonly string[] Params =
    {
        "PARAMS:USERS", "PARAMS:USERS:CREATE", "PARAMS:USERS:UPDATE", "PARAMS:USERS:DELETE",
        "PARAMS:ROLES", "PARAMS:ROLES:CREATE", "PARAMS:ROLES:UPDATE", "PARAMS:ROLES:DELETE",
        "PARAMS:PRIVILEGES",
        "PARAMS:DB_ACCESS",
        "PARAMS:CLOCK_PARAMS",
        "PARAMS:CORRESPONDENCE",
        "PARAMS:CARDPAIE", "PARAMS:CARDPAIE:CREATE", "PARAMS:CARDPAIE:UPDATE", "PARAMS:CARDPAIE:DELETE",
        "PARAMS:CATEGORIES", "PARAMS:CATEGORIES:CREATE", "PARAMS:CATEGORIES:UPDATE", "PARAMS:CATEGORIES:DELETE",
        "PARAMS:AFFECTATIONS", "PARAMS:AFFECTATIONS:CREATE", "PARAMS:AFFECTATIONS:UPDATE", "PARAMS:AFFECTATIONS:DELETE",
        "PARAMS:TOLERANCES", "PARAMS:TOLERANCES:CREATE", "PARAMS:TOLERANCES:UPDATE",
        "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:CREATE", "PARAMS:HOLIDAYS:UPDATE", "PARAMS:HOLIDAYS:DELETE",
        "PARAMS:MAJORATIONS", "PARAMS:MAJORATIONS:UPDATE",
        "PARAMS:CODE_CONSTANTES",
        "PARAMS:ABSENCE_CODES", "PARAMS:ABSENCE_CODES:CREATE", "PARAMS:ABSENCE_CODES:UPDATE"
    };

    private static readonly string[] Proc =
    {
        "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE", "PROC:SHIFTS:DELETE",
        "PROC:EMPLOYEE_PLANNING", "PROC:EMPLOYEE_PLANNING:CREATE", "PROC:EMPLOYEE_PLANNING:UPDATE", "PROC:EMPLOYEE_PLANNING:DELETE",
        "PROC:PUNCHES",
        "PROC:ANOMALIES", "PROC:ANOMALIES:DELETE",
        "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
        "PROC:WEEKLY_VALIDATION",
        "PROC:OVERTIME"
    };

    private static readonly string[] Other = { "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES" };

    private static BddSage Sage(string? name) => new() { NomBd = name };
    private static BddPointeuse Pointeuse(string? name) => new() { NomBd = name };

    private static List<string> Build(int[] rights, string[]? sages = null, string[]? pointeuses = null)
        => Defaults(rights.ToHashSet(), (sages ?? Array.Empty<string>()).Select(Sage), (pointeuses ?? Array.Empty<string>()).Select(Pointeuse));

    #region BuildDefaultPermissionsList

    [Fact]
    public void Build_NoGateNoDatabase_ReturnsEmpty()
    {
        // Act
        var result = Build(Array.Empty<int>());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_Gate1WithoutDatabase_GivesTheTwoCreateCodes()
    {
        // Act
        var result = Build(new[] { Rights.Databases });

        // Assert
        result.Should().Equal(new[] { "SAGE:CREATE", "POINTEUSE:CREATE" });
    }

    [Fact]
    public void Build_Gate1WithDatabases_GivesCreateThenCrudPerDatabase()
    {
        // Act
        var result = Build(new[] { Rights.Databases }, new[] { "A", "B" }, new[] { "P" });

        // Assert
        result.Should().Equal(new[]
        {
            "SAGE:CREATE",
            "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE",
            "SAGE:B:READ", "SAGE:B:UPDATE", "SAGE:B:DELETE",
            "POINTEUSE:CREATE",
            "POINTEUSE:P:READ", "POINTEUSE:P:UPDATE", "POINTEUSE:P:DELETE"
        });
    }

    [Fact]
    public void Build_SageListWithoutGate1_GivesSageCodesWithoutPointeuse()
    {
        // Act
        var result = Build(new[] { Rights.Processing }, new[] { "A" }, new[] { "P" });

        // Assert
        result.Should().Equal(new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE" }.Concat(Proc));
    }

    [Fact]
    public void Build_BlankDatabaseNames_AreSkipped()
    {
        // Act
        var result = Build(new[] { Rights.Databases }, new[] { "A", "", " " }, new[] { "" });

        // Assert
        result.Should().Equal(new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE", "POINTEUSE:CREATE" });
    }

    [Fact]
    public void Build_SingleGate_GivesItsFixedList()
    {
        // Act & Assert
        Build(new[] { Rights.Parameters }).Should().Equal(Params);
        Build(new[] { Rights.Processing }).Should().Equal(Proc);
        Build(new[] { Rights.Other }).Should().Equal(Other);
        Build(new[] { Rights.Traceability }).Should().Equal(new[] { "TRACE:AUDIT" });
        Build(new[] { Rights.Reports }).Should().Equal(new[] { "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING" });
    }

    [Fact]
    public void Build_AllGates_ConcatenatesInGateOrder()
    {
        // Arrange
        var all = new[] { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };

        // Act
        var expected = new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE", "POINTEUSE:CREATE", "POINTEUSE:P:READ", "POINTEUSE:P:UPDATE", "POINTEUSE:P:DELETE" }
            .Concat(Params).Concat(Proc).Concat(Other).Concat(new[] { "TRACE:AUDIT", "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING" });

        // Assert
        Build(all, new[] { "A" }, new[] { "P" }).Should().Equal(expected);
    }

    #endregion

    #region DeduirePortes

    [Fact]
    public void Gates_EachPrefix_GivesItsGate()
    {
        // Act & Assert
        Gates(new[] { "SAGE:A:READ" }).Should().Equal(new decimal[] { 1 });
        Gates(new[] { "POINTEUSE:CREATE" }).Should().Equal(new decimal[] { 1 });
        Gates(new[] { "PARAMS:USERS" }).Should().Equal(new decimal[] { 2 });
        Gates(new[] { "PROC:SHIFTS" }).Should().Equal(new decimal[] { 3 });
        Gates(new[] { "OTHER:CANTEEN" }).Should().Equal(new decimal[] { 4 });
        Gates(new[] { "TRACE:AUDIT" }).Should().Equal(new decimal[] { 5 });
        Gates(new[] { "REPORTS:REPORTS" }).Should().Equal(new decimal[] { 6 });
        Gates(new[] { "REPORTS:PREDICTIONS" }).Should().Equal(new decimal[] { 6 });
        PermissionCatalog.IsKnown("REPORTS:PREDICTIONS").Should().BeTrue();
    }

    [Fact]
    public void Gates_MixedCaseUnknownOrDuplicatedPrefixes_AreHandled()
    {
        // Act
        var result = Gates(new[] { "sage:a:read", "proc:shifts", "PROC:SHIFTS", "INCONNU:X", "" }).OrderBy(x => x);

        // Assert
        result.Should().Equal(new decimal[] { 1, 3 }.OrderBy(x => x));
        Gates(Array.Empty<string>()).Should().BeEmpty();
        Gates(new[] { "INCONNU:X", "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__" }).Should().BeEmpty();
    }

    [Fact]
    public void Gates_FullDefaultList_GivesAllSix()
    {
        // Arrange
        var all = new[] { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };

        // Act
        var result = Gates(Build(all, new[] { "A" }, new[] { "P" })).OrderBy(x => x);

        // Assert
        result.Should().Equal(new decimal[] { 1, 2, 3, 4, 5, 6 });
    }

    #endregion
}
