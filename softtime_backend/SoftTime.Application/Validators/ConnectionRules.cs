using FluentValidation;

namespace SoftTime.Application.Validators;

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
