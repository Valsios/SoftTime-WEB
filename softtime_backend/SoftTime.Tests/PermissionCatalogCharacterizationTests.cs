using SoftTime.Application.Security;
using SoftTime.Domain;

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

    private static List<string> Build(params int[] rights) => PermissionCatalog.BuildDefaultPermissionsList(rights.ToHashSet());

    private static readonly string[] AllDatabases =
    {
        "sage.create", "sage.all.read", "sage.all.update", "sage.all.delete",
        "pointeuse.create", "pointeuse.all.read", "pointeuse.all.update", "pointeuse.all.delete"
    };

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
    [InlineData("sage.12.read", true)]
    [InlineData("POINTEUSE.3.DELETE", true)]
    [InlineData("sage.create", true)]
    [InlineData("SHIFTS.READ", true)]
    [InlineData("self.read", true)]
    [InlineData("sage.all.update", true)]
    [InlineData("sage.PAIE.read", false)]
    [InlineData("sage.0.read", false)]
    [InlineData("sage.1.2.read", false)]
    [InlineData("sage.1.write", false)]
    [InlineData("sage..read", false)]
    [InlineData("SAGE:A:READ", false)]
    [InlineData("sage", false)]
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
        "users.read", "users.create", "users.update", "users.delete",
        "roles.read", "roles.create", "roles.update", "roles.delete",
        "privileges.read",
        "db-access.read",
        "clock-params.manage",
        "correspondence.manage",
        "card-paie.read", "card-paie.create", "card-paie.update", "card-paie.delete",
        "categories.read", "categories.create", "categories.update", "categories.delete",
        "affectations.read", "affectations.create", "affectations.update", "affectations.delete",
        "tolerances.read", "tolerances.create", "tolerances.update",
        "holidays.read", "holidays.create", "holidays.update", "holidays.delete",
        "majorations.read", "majorations.update",
        "code-constantes.manage",
        "absence-codes.read", "absence-codes.create", "absence-codes.update"
    };

    private static readonly string[] Proc =
    {
        "shifts.read", "shifts.create", "shifts.update", "shifts.delete",
        "employee-planning.read", "employee-planning.create", "employee-planning.update", "employee-planning.delete",
        "punches.manage",
        "anomalies.manage", "anomalies.delete",
        "corrected-hours.read", "corrected-hours.create", "corrected-hours.update", "corrected-hours.delete",
        "weekly-validation.manage",
        "overtime.manage"
    };

    private static readonly string[] Other = { "canteen.manage", "travel-expenses.manage" };

    #region BuildDefaultPermissionsList

    [Fact]
    public void Build_NoGateNoDatabase_ReturnsEmpty()
    {
        // Act
        var result = Build();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_Gate1_GivesCreateAndEveryActionOnAllDatabasesIncludingFutureOnes()
    {
        // Act
        var result = Build(Rights.Databases);

        // Assert
        result.Should().Equal(AllDatabases);
    }

    [Fact]
    public void Build_SingleGate_GivesItsFixedList()
    {
        // Act & Assert
        Build(Rights.Parameters).Should().Equal(Params);
        Build(Rights.Processing).Should().Equal(Proc);
        Build(Rights.Other).Should().Equal(Other);
        Build(Rights.Traceability).Should().Equal(new[] { "audit.read" });
        Build(Rights.Reports).Should().Equal(new[] { "reports.read", "predictions.read", "closing.read" });
    }

    [Fact]
    public void Build_AllGates_ConcatenatesInGateOrder()
    {
        // Arrange
        var all = new[] { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };

        // Act
        var expected = AllDatabases
            .Concat(Params).Concat(Proc).Concat(Other).Concat(new[] { "audit.read", "reports.read", "predictions.read", "closing.read" });

        // Assert
        Build(all).Should().Equal(expected);
    }

    #endregion

    [Theory]
    [InlineData("categories.delete", "categories.read")]
    [InlineData("anomalies.delete", "anomalies.manage")]
    [InlineData("users.create", "users.read")]
    [InlineData("categories.read", null)]
    [InlineData("sage.1.update", null)]
    [InlineData("inconnu.delete", null)]
    public void PageOf_Code_GivesThePageItsScreenRequires(string code, string? page)
    {
        // Act
        var result = PermissionCatalog.PageOf(code);

        // Assert
        result.Should().Be(page);
    }

    #region DeduirePortes

    [Fact]
    public void Gates_EachPrefix_GivesItsGate()
    {
        // Act & Assert
        Gates(new[] { "sage.A.read" }).Should().Equal(new decimal[] { 1 });
        Gates(new[] { "pointeuse.create" }).Should().Equal(new decimal[] { 1 });
        Gates(new[] { "users.read" }).Should().Equal(new decimal[] { 2 });
        Gates(new[] { "shifts.read" }).Should().Equal(new decimal[] { 3 });
        Gates(new[] { "canteen.manage" }).Should().Equal(new decimal[] { 4 });
        Gates(new[] { "audit.read" }).Should().Equal(new decimal[] { 5 });
        Gates(new[] { "reports.read" }).Should().Equal(new decimal[] { 6 });
        Gates(new[] { "predictions.read" }).Should().Equal(new decimal[] { 6 });
        PermissionCatalog.IsKnown("predictions.read").Should().BeTrue();
    }

    [Fact]
    public void Gates_MixedCaseUnknownOrDuplicatedPrefixes_AreHandled()
    {
        // Act
        var result = Gates(new[] { "sage.a.read", "SHIFTS.READ", "shifts.read", "INCONNU:X", "" }).OrderBy(x => x);

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
        var result = Gates(Build(all)).OrderBy(x => x);

        // Assert
        result.Should().Equal(new decimal[] { 1, 2, 3, 4, 5, 6 });
    }

    #endregion
}
