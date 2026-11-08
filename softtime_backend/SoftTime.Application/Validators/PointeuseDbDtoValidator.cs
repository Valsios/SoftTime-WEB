using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class PointeuseDbDtoValidator : AbstractValidator<PointeuseDbDto>
{
    public PointeuseDbDtoValidator()
    {
        ConnectionRules.Apply(this);
        RuleFor(x => x.TypePointage).MaximumLength(50).WithMessage("Le type de pointage ne peut pas dépasser 50 caractères.");
    }
}
