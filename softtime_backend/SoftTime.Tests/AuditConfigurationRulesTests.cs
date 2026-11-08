using SoftTime.Domain.Services;

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
        // Act & Assert
        AuditConfigurationRules.IsValid(type).Should().BeTrue();
        AuditConfigurationRules.IsValid(type.ToUpperInvariant()).Should().BeTrue();
        AuditConfigurationRules.IsValid($"  {type}  ").Should().BeTrue();
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
        // Act
        var result = AuditConfigurationRules.IsValid(type);

        // Assert
        result.Should().BeFalse();
        AuditConfigurationRules.IsExcluded(type).Should().BeTrue();
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
        // Act
        var result = AuditConfigurationRules.IsValid(type);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Normalize_PaddedMixedCaseType_TrimsAndLowercases()
    {
        // Act & Assert
        AuditConfigurationRules.Normalize("  NVARCHAR  ").Should().Be("nvarchar");
        AuditConfigurationRules.Normalize("INT").Should().Be("int");
        AuditConfigurationRules.Normalize(" DateTime ").Should().Be("datetime");
    }

    [Fact]
    public void Normalize_WithNullOrInvalid_ThrowsDomainException()
    {
        // Act & Assert
        FluentActions.Invoking(() => AuditConfigurationRules.Normalize(null!)).Should().ThrowExactly<DomainException>();
        FluentActions.Invoking(() => AuditConfigurationRules.Normalize("not_a_type")).Should().ThrowExactly<DomainException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Valider_WithEmptyOrNull_ThrowsDomainException(string? type)
    {
        // Act
        var act = () => AuditConfigurationRules.Valider(type);

        // Assert
        var ex = act.Should().ThrowExactly<DomainException>().Which;
        ex.Message.Should().ContainEquivalentOf("obligatoire");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("ntext")]
    [InlineData("image")]
    public void Valider_WithExcludedType_ThrowsDomainException(string type)
    {
        // Act
        var act = () => AuditConfigurationRules.Valider(type);

        // Assert
        var ex = act.Should().ThrowExactly<DomainException>().Which;
        ex.Message.Should().ContainEquivalentOf("exclu");
    }

    [Theory]
    [InlineData("inconnu")]
    [InlineData("custom_sql_type")]
    public void Valider_WithUnknownType_ThrowsDomainException(string type)
    {
        // Act
        var act = () => AuditConfigurationRules.Valider(type);

        // Assert
        var ex = act.Should().ThrowExactly<DomainException>().Which;
        ex.Message.Should().ContainEquivalentOf("pas autorisé");
    }

    [Fact]
    public void Valider_WithValidType_DoesNotThrow()
    {
        AuditConfigurationRules.Valider("varchar");
        AuditConfigurationRules.Valider("  INT  ");
        AuditConfigurationRules.Valider("DateTime2");
    }

    [Fact]
    public void AllowedTypeOptions_Catalog_HasExpectedItemsWithoutDuplicatesOrExcludedTypes()
    {
        // Act
        var options = AuditConfigurationRules.AllowedTypeOptions;

        // Assert
        options.Should().NotBeEmpty();
        options.Select(o => o.Value).Distinct().Count().Should().Be(options.Count);
        options.Where(o => o.Value is "text" or "ntext" or "image").Should().BeEmpty();
        options.Should().Contain(o => o.Value == "varchar");
        options.Should().Contain(o => o.Value == "nvarchar");
        options.Should().Contain(o => o.Value == "int");
    }
}
