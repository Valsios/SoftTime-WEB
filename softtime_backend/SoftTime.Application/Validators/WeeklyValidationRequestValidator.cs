using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class WeeklyValidationRequestValidator : AbstractValidator<WeeklyValidationRequest>
{
    public WeeklyValidationRequestValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("La date de fin doit suivre la date de début.");
    }
}
