using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class TestPointeuseConnectionDtoValidator : AbstractValidator<TestPointeuseConnectionDto>
{
    public TestPointeuseConnectionDtoValidator()
        => ConnectionRules.Apply(this);
}
