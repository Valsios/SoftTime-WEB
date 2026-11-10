using SoftTime.Application.Security;

namespace SoftTime.Tests;

/// <summary>Validateur commun des codes de permission (rôles et utilisateurs).</summary>
public class PermissionCodeValidatorTests
{
    private const string Marker = "__MARKER__";

    [Theory]
    [InlineData("PROC:SHIFTS:DELETE", "PROC:SHIFTS:DELETE")]
    [InlineData("  proc:shifts:delete ", "PROC:SHIFTS:DELETE")]
    [InlineData("self:read", "SELF:READ")]
    [InlineData("sage:create", "SAGE:CREATE")]
    [InlineData("pointeuse:create", "POINTEUSE:CREATE")]
    [InlineData("sage:Paie_2025:read", "SAGE:Paie_2025:READ")]
    [InlineData("POINTEUSE:ptg:Delete", "POINTEUSE:ptg:DELETE")]
    [InlineData("sage:AUTRE:create", "SAGE:AUTRE:CREATE")]
    public void Normalize_ValidCode_ReturnsNormalizedCode(string input, string expected)
    {
        // Act
        var result = PermissionCodeValidator.Normalize(new[] { input }, Marker);

        // Assert
        result.Should().Equal(expected);
    }

    [Theory]
    [InlineData("INVALID:HACK:CODE")]
    [InlineData("PROC:SHIFTS:PURGE")]
    [InlineData("SAGE:")]
    [InlineData("SAGE:BASE")]
    [InlineData("SAGE:BASE:EXECUTE")]
    [InlineData("POINTEUSE::READ")]
    public void Normalize_UnknownCode_ThrowsWithCodeInMessage(string code)
    {
        // Act
        var act = () => PermissionCodeValidator.Normalize(new[] { code }, Marker);

        // Assert
        act.Should().ThrowExactly<DomainException>().WithMessage("Codes de permission inconnus : *");
    }

    [Fact]
    public void Normalize_CodeLongerThan128_Throws()
    {
        // Arrange
        var code = "SAGE:" + new string('A', 119) + ":READ";

        // Act
        var act = () => PermissionCodeValidator.Normalize(new[] { code }, Marker);

        // Assert
        act.Should().ThrowExactly<DomainException>().WithMessage("Code de permission trop long (128 caractères maximum) : *");
    }

    [Fact]
    public void Normalize_CaseDuplicatesBlanksAndMarker_AreRemoved()
    {
        // Act
        var result = PermissionCodeValidator.Normalize(new[] { "PARAMS:USERS", "params:users", " ", Marker }, Marker);

        // Assert
        result.Should().Equal("PARAMS:USERS");
    }

    [Fact]
    public void Normalize_NullList_Throws()
    {
        // Act
        var act = () => PermissionCodeValidator.Normalize(null, Marker);

        // Assert
        act.Should().ThrowExactly<DomainException>().WithMessage("La liste des permissions ne peut pas être nulle.");
    }

    [Theory]
    [InlineData("PROC:SHIFTS", true)]
    [InlineData("proc:shifts", true)]
    [InlineData("SAGE:B:UPDATE", true)]
    [InlineData("SAGE:B", false)]
    [InlineData("OBSOLETE:CODE", false)]
    [InlineData("", false)]
    public void IsValid_Code_MatchesNormalizeRule(string code, bool expected)
    {
        // Act
        var result = PermissionCodeValidator.IsValid(code);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("SAGE:ANCIENNE", true)]
    [InlineData("POINTEUSE:PTG", true)]
    [InlineData("SAGE:B:READ", false)]
    [InlineData("SAGE:CREATE", false)]
    [InlineData("OBSOLETE:CODE", false)]
    [InlineData("PROC:SHIFTS", false)]
    public void IsLegacyDatabaseCode_Code_TrueOnlyForDatabaseCodesWithoutKnownAction(string code, bool expected)
    {
        // Act
        var result = PermissionCodeValidator.IsLegacyDatabaseCode(code);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void WithLegacyDatabaseCodes_StoredRows_KeepsOnlyLegacyDatabaseCodes()
    {
        // Arrange
        var validated = new List<string> { "PROC:SHIFTS" };
        var stored = new[] { "SAGE:ANCIENNE", "OBSOLETE:CODE", "PARAMS:USERS", "sage:ancienne" };

        // Act
        var result = PermissionCodeValidator.WithLegacyDatabaseCodes(validated, stored);

        // Assert
        result.Should().Equal("PROC:SHIFTS", "SAGE:ANCIENNE");
    }
}
