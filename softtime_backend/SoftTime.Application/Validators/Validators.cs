using FluentValidation;
using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Login).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class PeriodRequestValidator : AbstractValidator<PeriodRequest>
{
    public PeriodRequestValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
    }
}

public class WeeklyValidationRequestValidator : AbstractValidator<WeeklyValidationRequest>
{
    public WeeklyValidationRequestValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
    }
}
