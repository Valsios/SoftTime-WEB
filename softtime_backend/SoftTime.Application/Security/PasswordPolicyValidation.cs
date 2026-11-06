namespace SoftTime.Application.Security;

/// <summary>Résultat complet de l'évaluation d'un mot de passe candidat.</summary>
public sealed record PasswordPolicyValidation(bool IsValid, IReadOnlyList<PasswordPolicyViolation> Violations)
{
    /// <summary>Message prêt à être renvoyé au client (HTTP 400) ou affiché dans l'interface.</summary>
    public string ErrorMessage => IsValid
        ? string.Empty
        : "Le mot de passe ne respecte pas les règles de sécurité : "
          + string.Join(" ; ", Violations.Select(v => v.Message)) + ".";
}
