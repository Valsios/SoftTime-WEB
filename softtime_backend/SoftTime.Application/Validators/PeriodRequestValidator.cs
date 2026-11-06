using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class PeriodRequestValidator : AbstractValidator<PeriodRequest>
{
    public PeriodRequestValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("La date de fin doit suivre la date de début.");
    }
}
