using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Pas de « requis » ici : un champ vide reste un échec de connexion ordinaire (401, même message).
        RuleFor(x => x.Login).MaximumLength(Limits.Login).WithMessage($"Le login ne peut pas dépasser {Limits.Login} caractères.");
        RuleFor(x => x.Password).MaximumLength(Limits.Password).WithMessage($"Le mot de passe ne peut pas dépasser {Limits.Password} caractères.");
    }
}
