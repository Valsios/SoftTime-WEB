using System;
using System.Linq;
using SoftTime.Domain.Services;
using Xunit;

namespace SoftTime.Tests;

public class AuditConfigurationRulesTests
{
    [Theory]
    [InlineData("varchar")]
    [InlineData("nvarchar")]
    [InlineData("int")]
    [InlineData("smallint")]
    [InlineData("tinyint")]
    [InlineData("bigint")]
    [InlineData("decimal")]
    [InlineData("numeric")]
    [InlineData("float")]
    [InlineData("real")]
    [InlineData("money")]
    [InlineData("smallmoney")]
    [InlineData("datetime")]
    [InlineData("datetime2")]
    [InlineData("smalldatetime")]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("bit")]
    [InlineData("char")]
    [InlineData("nchar")]
    [InlineData("uniqueidentifier")]
    public void IsValid_WithStandardTypes_ReturnsTrue(string type)
    {
        Assert.True(AuditConfigurationRules.IsValid(type));
        Assert.True(AuditConfigurationRules.IsValid(type.ToUpperInvariant()));
        Assert.True(AuditConfigurationRules.IsValid($"  {type}  "));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("ntext")]
    [InlineData("image")]
    [InlineData("TEXT")]
    [InlineData("NTEXT")]
    [InlineData("IMAGE")]
    [InlineData("  image  ")]
    public void IsValid_WithExcludedLegacyTypes_ReturnsFalse(string type)
    {
        Assert.False(AuditConfigurationRules.IsValid(type));
        Assert.True(AuditConfigurationRules.IsExcluded(type));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("unknown_type")]
    [InlineData("blob")]
    [InlineData("json")]
    [InlineData("variant")]
    public void IsValid_WithInvalidOrEmptyType_ReturnsFalse(string? type)
    {
        Assert.False(AuditConfigurationRules.IsValid(type));
    }

    [Fact]
    public void Normalize_TrimsAndLowercases()
    {
        Assert.Equal("nvarchar", AuditConfigurationRules.Normalize("  NVARCHAR  "));
        Assert.Equal("int", AuditConfigurationRules.Normalize("INT"));
        Assert.Equal("datetime", AuditConfigurationRules.Normalize(" DateTime "));
    }

    [Fact]
    public void Normalize_WithNullOrInvalid_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => AuditConfigurationRules.Normalize(null!));
        Assert.Throws<InvalidOperationException>(() => AuditConfigurationRules.Normalize("not_a_type"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Valider_WithEmptyOrNull_ThrowsInvalidOperationException(string? type)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuditConfigurationRules.Valider(type));
        Assert.Contains("obligatoire", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("ntext")]
    [InlineData("image")]
    public void Valider_WithExcludedType_ThrowsInvalidOperationException(string type)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuditConfigurationRules.Valider(type));
        Assert.Contains("exclu", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("inconnu")]
    [InlineData("custom_sql_type")]
    public void Valider_WithUnknownType_ThrowsInvalidOperationException(string type)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuditConfigurationRules.Valider(type));
        Assert.Contains("pas autorisé", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valider_WithValidType_DoesNotThrow()
    {
        AuditConfigurationRules.Valider("varchar");
        AuditConfigurationRules.Valider("  INT  ");
        AuditConfigurationRules.Valider("DateTime2");
    }

    [Fact]
    public void AllowedTypeOptions_HasExpectedItemsWithoutDuplicatesOrExcludedTypes()
    {
        var options = AuditConfigurationRules.AllowedTypeOptions;

        Assert.NotEmpty(options);
        Assert.Equal(options.Count, options.Select(o => o.Value).Distinct().Count());
        Assert.DoesNotContain(options, o => o.Value is "text" or "ntext" or "image");
        Assert.Contains(options, o => o.Value == "varchar");
        Assert.Contains(options, o => o.Value == "nvarchar");
        Assert.Contains(options, o => o.Value == "int");
    }
}
