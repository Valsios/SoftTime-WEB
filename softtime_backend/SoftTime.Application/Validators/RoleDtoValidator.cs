using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class RoleDtoValidator : AbstractValidator<RoleDto>
{
    public RoleDtoValidator()
    {
        RuleFor(x => x.Nom)
            .MaximumLength(Limits.Name).WithMessage($"Le nom du rôle ne peut pas dépasser {Limits.Name} caractères.")
            .Must(Limits.HasNoControlCharacter).WithMessage("Le nom du rôle contient un caractère non autorisé.");
    }
}
