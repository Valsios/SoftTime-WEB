namespace SoftTime.Application.Security;

/// <summary>Une règle non respectée par un mot de passe candidat.</summary>
public sealed record PasswordPolicyViolation(string Code, string Message);
