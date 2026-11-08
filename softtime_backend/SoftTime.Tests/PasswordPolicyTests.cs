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

        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
        result.ErrorMessage.Should().Be(string.Empty);
    }

    [Fact]
    public void Validate_WithExactlyMinimumLength_IsValid()
    {
        var result = PasswordPolicy.Validate("Aa1!Bb2#");

        "Aa1!Bb2#".Length.Should().Be(PasswordPolicy.MinimumLength);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithAccentedCharacters_IsValid()
    {
        var result = PasswordPolicy.Validate("Été2026!");

        result.IsValid.Should().BeTrue();
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

        result.IsValid.Should().BeFalse();
        result.Violations.Select(v => v.Code).Should().Equal(new[] { "length" });
    }

    [Fact]
    public void Validate_WithoutUppercase_ReportsUppercaseViolationOnly()
    {
        var result = PasswordPolicy.Validate("abcdefg1!");

        result.IsValid.Should().BeFalse();
        result.Violations.Select(v => v.Code).Should().Equal(new[] { "uppercase" });
    }

    [Fact]
    public void Validate_WithoutLowercase_ReportsLowercaseViolationOnly()
    {
        var result = PasswordPolicy.Validate("ABCDEFG1!");

        result.IsValid.Should().BeFalse();
        result.Violations.Select(v => v.Code).Should().Equal(new[] { "lowercase" });
    }

    [Fact]
    public void Validate_WithoutDigit_ReportsDigitViolationOnly()
    {
        var result = PasswordPolicy.Validate("Abcdefgh!");

        result.IsValid.Should().BeFalse();
        result.Violations.Select(v => v.Code).Should().Equal(new[] { "digit" });
    }

    [Fact]
    public void Validate_WithoutSpecialCharacter_ReportsSpecialViolationOnly()
    {
        // L'espace ne compte pas comme caractère spécial.
        var result = PasswordPolicy.Validate("Abcdefg1 ");

        result.IsValid.Should().BeFalse();
        result.Violations.Select(v => v.Code).Should().Equal(new[] { "special" });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyPassword_ReportsRequiredViolation(string? password)
    {
        var result = PasswordPolicy.Validate(password);

        result.IsValid.Should().BeFalse();
        result.Violations.Select(v => v.Code).Should().Equal(new[] { "required" });
    }

    [Fact]
    public void Validate_ReportsAllBrokenRulesAtOnce()
    {
        var result = PasswordPolicy.Validate("abc");

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Code == "length");
        result.Violations.Should().Contain(v => v.Code == "uppercase");
        result.Violations.Should().Contain(v => v.Code == "digit");
        result.Violations.Should().Contain(v => v.Code == "special");
    }

    [Fact]
    public void ErrorMessage_IsReadableAndListsViolations()
    {
        var result = PasswordPolicy.Validate("abc");

        result.ErrorMessage.Should().StartWith("Le mot de passe ne respecte pas les règles de sécurité : ");
        result.ErrorMessage.Should().Contain("au moins 8 caractères");
        result.ErrorMessage.Should().EndWith(".");
    }

    [Fact]
    public void EnsureValid_WithWeakPassword_ThrowsDomainException()
    {
        var ex = FluentActions.Invoking(() => PasswordPolicy.EnsureValid("password")).Should().ThrowExactly<DomainException>().Which;

        ex.Message.Should().Contain("règles de sécurité");
    }

    // ---------- Descripteur exposé au frontend ----------

    [Fact]
    public void Describe_ExposesRulesAndLimitsUsedByTheClient()
    {
        var policy = PasswordPolicy.Describe();

        policy.MinLength.Should().Be(PasswordPolicy.MinimumLength);
        policy.Rules.Select(r => r.Code).Should().Equal(new[] { "length", "uppercase", "lowercase", "digit", "special" });
        policy.Rules.Should().AllSatisfy(rule => string.IsNullOrWhiteSpace(rule.Label).Should().BeFalse());
    }
}
