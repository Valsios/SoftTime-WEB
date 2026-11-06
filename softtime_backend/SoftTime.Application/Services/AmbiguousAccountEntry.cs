using SoftTime.Application.Security;

namespace SoftTime.Application.Services;

/// <summary>
/// Information sur un compte dont la valeur de mot de passe est ambiguë, de format externe ou mal formée.
/// Ces comptes sont scrupuleusement préservés sans modification pour empêcher toute corruption irréversible.
/// </summary>
public sealed record AmbiguousAccountEntry(
    decimal UserId,
    string? Login,
    PasswordClassification Classification,
    string Diagnostic);
