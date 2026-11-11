using System.Reflection;

using SoftTime.Application.Security;

namespace SoftTime.Tests;

/// <summary>Empreinte des droits écrite dans le jeton, et constantes de permissions utilisées par [HasPermission].</summary>
public class RightsStampTests
{
    private static EffectiveRights Compute(params string[] roleCodes)
        => EffectiveRightsCalculator.ComputeEffectiveRights(
            new RoleDefinition(50, true, roleCodes.Select(code => new PermissionRow(code)).ToList(), Array.Empty<int>()),
            Array.Empty<PermissionRow>(),
            new DatabaseCatalog(new[] { "PAIE" }, new[] { "PTG" }, new[] { "PAIE" }));

    [Fact]
    public void Stamp_SameRights_IsIdenticalWhateverTheOrder()
    {
        // Arrange
        var first = Compute("PROC:SHIFTS", "PARAMS:CATEGORIES");
        var second = Compute("PARAMS:CATEGORIES", "PROC:SHIFTS");

        // Act
        var same = first.Stamp == second.Stamp;

        // Assert
        same.Should().BeTrue();
        first.Stamp.Should().HaveLength(16);
    }

    [Fact]
    public void Stamp_PermissionRemoved_Changes()
    {
        // Arrange
        var before = Compute("PROC:SHIFTS", "PROC:SHIFTS:DELETE");
        var after = Compute("PROC:SHIFTS");

        // Act
        var current = RightsStamp.IsCurrent(before.Stamp, after);

        // Assert
        current.Should().BeFalse();
        RightsStamp.IsCurrent(after.Stamp, after).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsCurrent_MissingStamp_IsNeverCurrent(string? tokenStamp)
    {
        // Arrange
        var rights = Compute("PROC:SHIFTS");

        // Act
        var current = RightsStamp.IsCurrent(tokenStamp, rights);

        // Assert
        current.Should().BeFalse();
    }

    [Fact]
    public void TokenPermissions_DatabaseCodes_AreLeftOut()
    {
        // Arrange
        var rights = Compute("PROC:SHIFTS", "SAGE:PAIE:READ", "POINTEUSE:PTG:READ");

        // Act
        var tokenPermissions = rights.TokenPermissions;

        // Assert
        tokenPermissions.Should().BeEquivalentTo("PROC:SHIFTS");
    }

    [Fact]
    public void Permissions_EveryConstant_IsKnownByTheCatalog()
    {
        // Arrange
        var codes = typeof(Permissions).GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        // Act
        var unknown = codes.Where(code => !PermissionCatalog.KnownStatic.Contains(code)).ToList();

        // Assert
        codes.Should().NotBeEmpty();
        unknown.Should().BeEmpty();
    }
}
