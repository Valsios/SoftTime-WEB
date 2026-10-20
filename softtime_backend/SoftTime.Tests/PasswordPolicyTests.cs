using SoftTime.Application.Security;

namespace SoftTime.Tests;

public class PasswordPolicyTests
{
    private const string StrongPassword = "Aa1!Bb2#";

    // ---------- Mots de passe acceptés ----------

    [Fact]
    public void Validate_WithStrongPassword_ReturnsValid()
    {
        var result = PasswordPolicy.Validate(StrongPassword);

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(string.Empty, result.ErrorMessage);
    }

    [Fact]
    public void Validate_WithExactlyMinimumLength_IsValid()
    {
        var result = PasswordPolicy.Validate("Aa1!Bb2#");

        Assert.Equal(PasswordPolicy.MinimumLength, "Aa1!Bb2#".Length);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithAccentedCharacters_IsValid()
    {
        var result = PasswordPolicy.Validate("Été2026!");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EnsureValid_WithStrongPassword_DoesNotThrow()
    {
        PasswordPolicy.EnsureValid(StrongPassword);
    }

    // ---------- Chaque règle prise isolément ----------

    [Fact]
    public void Validate_WithTooShortPassword_ReportsLengthViolation()
    {
        var result = PasswordPolicy.Validate("Ab1!xY");

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "length" }, result.Violations.Select(v => v.Code));
    }

    [Fact]
    public void Validate_WithoutUppercase_ReportsUppercaseViolationOnly()
    {
        var result = PasswordPolicy.Validate("abcdefg1!");

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "uppercase" }, result.Violations.Select(v => v.Code));
    }

    [Fact]
    public void Validate_WithoutLowercase_ReportsLowercaseViolationOnly()
    {
        var result = PasswordPolicy.Validate("ABCDEFG1!");

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "lowercase" }, result.Violations.Select(v => v.Code));
    }

    [Fact]
    public void Validate_WithoutDigit_ReportsDigitViolationOnly()
    {
        var result = PasswordPolicy.Validate("Abcdefgh!");

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "digit" }, result.Violations.Select(v => v.Code));
    }

    [Fact]
    public void Validate_WithoutSpecialCharacter_ReportsSpecialViolationOnly()
    {
        // L'espace ne compte pas comme caractère spécial.
        var result = PasswordPolicy.Validate("Abcdefg1 ");

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "special" }, result.Violations.Select(v => v.Code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyPassword_ReportsRequiredViolation(string? password)
    {
        var result = PasswordPolicy.Validate(password);

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "required" }, result.Violations.Select(v => v.Code));
    }

    [Fact]
    public void Validate_ReportsAllBrokenRulesAtOnce()
    {
        var result = PasswordPolicy.Validate("abc");

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Code == "length");
        Assert.Contains(result.Violations, v => v.Code == "uppercase");
        Assert.Contains(result.Violations, v => v.Code == "digit");
        Assert.Contains(result.Violations, v => v.Code == "special");
    }

    [Fact]
    public void ErrorMessage_IsReadableAndListsViolations()
    {
        var result = PasswordPolicy.Validate("abc");

        Assert.StartsWith("Le mot de passe ne respecte pas les règles de sécurité : ", result.ErrorMessage);
        Assert.Contains("au moins 8 caractères", result.ErrorMessage);
        Assert.EndsWith(".", result.ErrorMessage);
    }

    [Fact]
    public void EnsureValid_WithWeakPassword_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PasswordPolicy.EnsureValid("password"));

        Assert.Contains("règles de sécurité", ex.Message);
    }

    // ---------- Descripteur exposé au frontend ----------

    [Fact]
    public void Describe_ExposesRulesAndLimitsUsedByTheClient()
    {
        var policy = PasswordPolicy.Describe();

        Assert.Equal(PasswordPolicy.MinimumLength, policy.MinLength);
        Assert.Equal(
            new[] { "length", "uppercase", "lowercase", "digit", "special" },
            policy.Rules.Select(r => r.Code));
        Assert.All(policy.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Label)));
    }
}
