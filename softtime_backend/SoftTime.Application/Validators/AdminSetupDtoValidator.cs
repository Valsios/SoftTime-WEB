using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

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
