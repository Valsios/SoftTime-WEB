using FluentValidation;
using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

/// <summary>
/// Bornes communes des champs saisis. Les règles métier (champ requis, politique de mot de passe, login déjà pris)
/// restent dans les services, qui renvoient leurs propres messages ; ces validateurs refusent seulement les valeurs
/// démesurées ou mal formées avant tout traitement (exécutés par FluentValidationFilter dans l'API).
/// </summary>
internal static class Limits
{
    public const int Name = 100;
    public const int Login = 100;
    /// <summary>Un mot de passe plus long ne sert à rien (BCrypt ne lit que 72 octets) et coûte du calcul au hachage.</summary>
    public const int Password = 256;
    public const int Email = 254;
    public const int Matricule = 50;
    public const int Server = 256;
    /// <summary>Longueur maximale d'un nom SQL Server (sysname) : base, compte SQL.</summary>
    public const int SqlName = 128;
    public const int RefreshToken = 200;

    public static readonly string[] DatabaseTypes = { "STANDARD", "AUTRE" };

    public static bool HasNoControlCharacter(string? value) => value is null || !value.Any(char.IsControl);

    public static bool IsKnownDatabaseType(string? value)
        => value is null || DatabaseTypes.Contains(value, StringComparer.OrdinalIgnoreCase);
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Pas de « requis » ici : un champ vide reste un échec de connexion ordinaire (401, même message).
        RuleFor(x => x.Login).MaximumLength(Limits.Login).WithMessage($"Le login ne peut pas dépasser {Limits.Login} caractères.");
        RuleFor(x => x.Password).MaximumLength(Limits.Password).WithMessage($"Le mot de passe ne peut pas dépasser {Limits.Password} caractères.");
    }
}

public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).MaximumLength(Limits.RefreshToken).WithMessage("Jeton de rafraîchissement invalide.");
    }
}

public class AdminSetupDtoValidator : AbstractValidator<AdminSetupDto>
{
    public AdminSetupDtoValidator()
    {
        RuleFor(x => x.Nom).MaximumLength(Limits.Name).WithMessage($"Le nom ne peut pas dépasser {Limits.Name} caractères.");
        RuleFor(x => x.Login)
            .MaximumLength(Limits.Login).WithMessage($"Le login ne peut pas dépasser {Limits.Login} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le login contient un caractère non autorisé.");
        RuleFor(x => x.Password).MaximumLength(Limits.Password).WithMessage($"Le mot de passe ne peut pas dépasser {Limits.Password} caractères.");
        RuleFor(x => x.Email)
            .MaximumLength(Limits.Email).WithMessage($"L'adresse e-mail ne peut pas dépasser {Limits.Email} caractères.")
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("L'adresse e-mail n'est pas valide.");
    }
}

public class UserDtoValidator : AbstractValidator<UserDto>
{
    public UserDtoValidator()
    {
        RuleFor(x => x.Nom).MaximumLength(Limits.Name).WithMessage($"Le nom ne peut pas dépasser {Limits.Name} caractères.");
        RuleFor(x => x.Login)
            .MaximumLength(Limits.Login).WithMessage($"Le login ne peut pas dépasser {Limits.Login} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le login contient un caractère non autorisé.");
        RuleFor(x => x.Matricule).MaximumLength(Limits.Matricule).WithMessage($"Le matricule ne peut pas dépasser {Limits.Matricule} caractères.");
        RuleFor(x => x.Password).MaximumLength(Limits.Password).WithMessage($"Le mot de passe ne peut pas dépasser {Limits.Password} caractères.");
        RuleFor(x => x.Email)
            .MaximumLength(Limits.Email).WithMessage($"L'adresse e-mail ne peut pas dépasser {Limits.Email} caractères.")
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("L'adresse e-mail n'est pas valide.");
    }
}

public class RoleDtoValidator : AbstractValidator<RoleDto>
{
    public RoleDtoValidator()
    {
        RuleFor(x => x.Nom)
            .MaximumLength(Limits.Name).WithMessage($"Le nom du rôle ne peut pas dépasser {Limits.Name} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le nom du rôle contient un caractère non autorisé.");
    }
}

/// <summary>Connexion à une base externe (SAGE ou pointeuse) : mêmes bornes pour l'enregistrement et le test.</summary>
internal static class ConnectionRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string?>> serveur,
        System.Linq.Expressions.Expression<Func<T, string?>> login,
        System.Linq.Expressions.Expression<Func<T, string?>> password,
        System.Linq.Expressions.Expression<Func<T, string?>> nomBd,
        System.Linq.Expressions.Expression<Func<T, string?>> typeBase)
    {
        validator.RuleFor(serveur)
            .MaximumLength(Limits.Server).WithMessage($"Le nom du serveur ne peut pas dépasser {Limits.Server} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le nom du serveur contient un caractère non autorisé.");
        validator.RuleFor(login)
            .MaximumLength(Limits.SqlName).WithMessage($"Le login SQL ne peut pas dépasser {Limits.SqlName} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le login SQL contient un caractère non autorisé.");
        validator.RuleFor(password).MaximumLength(Limits.Password).WithMessage($"Le mot de passe ne peut pas dépasser {Limits.Password} caractères.");
        validator.RuleFor(nomBd)
            .MaximumLength(Limits.SqlName).WithMessage($"Le nom de la base ne peut pas dépasser {Limits.SqlName} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le nom de la base contient un caractère non autorisé.");
        validator.RuleFor(typeBase).Must(Limits.IsKnownDatabaseType).WithMessage("Type de base inconnu : STANDARD ou AUTRE.");
    }
}

public class SageDbDtoValidator : AbstractValidator<SageDbDto>
{
    public SageDbDtoValidator()
        => ConnectionRules.Apply(this, x => x.Serveur, x => x.Login, x => x.Password, x => x.NomBd, x => x.TypeBase);
}

public class PointeuseDbDtoValidator : AbstractValidator<PointeuseDbDto>
{
    public PointeuseDbDtoValidator()
    {
        ConnectionRules.Apply(this, x => x.Serveur, x => x.Login, x => x.Password, x => x.NomBd, x => x.TypeBase);
        RuleFor(x => x.TypePointage).MaximumLength(50).WithMessage("Le type de pointage ne peut pas dépasser 50 caractères.");
    }
}

public class TestSageConnectionDtoValidator : AbstractValidator<TestSageConnectionDto>
{
    public TestSageConnectionDtoValidator()
        => ConnectionRules.Apply(this, x => x.Serveur, x => x.Login, x => x.Password, x => x.NomBd, x => x.TypeBase);
}

public class TestPointeuseConnectionDtoValidator : AbstractValidator<TestPointeuseConnectionDto>
{
    public TestPointeuseConnectionDtoValidator()
        => ConnectionRules.Apply(this, x => x.Serveur, x => x.Login, x => x.Password, x => x.NomBd, x => x.TypeBase);
}

public class PeriodRequestValidator : AbstractValidator<PeriodRequest>
{
    public PeriodRequestValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("La date de fin doit suivre la date de début.");
    }
}

public class WeeklyValidationRequestValidator : AbstractValidator<WeeklyValidationRequest>
{
    public WeeklyValidationRequestValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("La date de fin doit suivre la date de début.");
    }
}
