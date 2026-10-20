using SoftTime.Application.DTOs;

namespace SoftTime.Application.Security;

/// <summary>Une règle non respectée par un mot de passe candidat.</summary>
public sealed record PasswordPolicyViolation(string Code, string Message);

/// <summary>Résultat complet de l'évaluation d'un mot de passe candidat.</summary>
public sealed record PasswordPolicyValidation(bool IsValid, IReadOnlyList<PasswordPolicyViolation> Violations)
{
    /// <summary>Message prêt à être renvoyé au client (HTTP 400) ou affiché dans l'interface.</summary>
    public string ErrorMessage => IsValid
        ? string.Empty
        : "Le mot de passe ne respecte pas les règles de sécurité : "
          + string.Join(" ; ", Violations.Select(v => v.Message)) + ".";
}

/// <summary>
/// Politique de mot de passe de Soft Time, appliquée à toute création ou modification
/// de mot de passe (création du premier administrateur et écran Utilisateurs).
///
/// Règles :
/// <list type="bullet">
///   <item>8 caractères minimum ;</item>
///   <item>au moins une majuscule, une minuscule, un chiffre et un caractère spécial.</item>
/// </list>
///
/// La même politique est exposée par <c>GET /api/auth/password-policy</c> afin que
/// l'interface Angular affiche exactement les règles appliquées par le serveur.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    private static readonly IReadOnlyList<PasswordPolicyRuleDto> Rules = new[]
    {
        new PasswordPolicyRuleDto("length", $"Au moins {MinimumLength} caractères"),
        new PasswordPolicyRuleDto("uppercase", "Au moins une majuscule"),
        new PasswordPolicyRuleDto("lowercase", "Au moins une minuscule"),
        new PasswordPolicyRuleDto("digit", "Au moins un chiffre"),
        new PasswordPolicyRuleDto("special", "Au moins un caractère spécial (!, @, #, …)")
    };

    /// <summary>Description de la politique, destinée au client.</summary>
    public static PasswordPolicyDto Describe() => new(MinimumLength, Rules);

    /// <summary>Valide un mot de passe candidat et renvoie toutes les règles non respectées.</summary>
    public static PasswordPolicyValidation Validate(string? password)
    {
        var value = password ?? string.Empty;
        var violations = new List<PasswordPolicyViolation>();

        if (string.IsNullOrWhiteSpace(value))
        {
            violations.Add(new PasswordPolicyViolation("required", "il est obligatoire"));
            return new PasswordPolicyValidation(false, violations);
        }

        if (value.Length < MinimumLength)
        {
            violations.Add(new PasswordPolicyViolation(
                "length", $"il doit contenir au moins {MinimumLength} caractères"));
        }

        if (!value.Any(char.IsUpper))
            violations.Add(new PasswordPolicyViolation("uppercase", "il doit contenir au moins une majuscule"));

        if (!value.Any(char.IsLower))
            violations.Add(new PasswordPolicyViolation("lowercase", "il doit contenir au moins une minuscule"));

        if (!value.Any(char.IsDigit))
            violations.Add(new PasswordPolicyViolation("digit", "il doit contenir au moins un chiffre"));

        if (!value.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
            violations.Add(new PasswordPolicyViolation("special", "il doit contenir au moins un caractère spécial"));

        return new PasswordPolicyValidation(violations.Count == 0, violations);
    }

    /// <summary>Valide le mot de passe et lève une <see cref="InvalidOperationException"/> (HTTP 400) si nécessaire.</summary>
    public static void EnsureValid(string? password)
    {
        var validation = Validate(password);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.ErrorMessage);
    }
}
