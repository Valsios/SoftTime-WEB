using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

/// <summary>Connexion à une base externe (SAGE ou pointeuse) : mêmes bornes pour l'enregistrement et le test.</summary>
internal static class ConnectionRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IExternalConnectionFields
    {
        validator.RuleFor(x => x.Serveur)
            .MaximumLength(Limits.Server).WithMessage($"Le nom du serveur ne peut pas dépasser {Limits.Server} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le nom du serveur contient un caractère non autorisé.");
        validator.RuleFor(x => x.Login)
            .MaximumLength(Limits.SqlName).WithMessage($"Le login SQL ne peut pas dépasser {Limits.SqlName} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le login SQL contient un caractère non autorisé.");
        validator.RuleFor(x => x.Password).MaximumLength(Limits.Password).WithMessage($"Le mot de passe ne peut pas dépasser {Limits.Password} caractères.");
        validator.RuleFor(x => x.NomBd)
            .MaximumLength(Limits.SqlName).WithMessage($"Le nom de la base ne peut pas dépasser {Limits.SqlName} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le nom de la base contient un caractère non autorisé.");
        validator.RuleFor(x => x.TypeBase).Must(Limits.IsKnownDatabaseType).WithMessage("Type de base inconnu : STANDARD ou AUTRE.");
    }
}
