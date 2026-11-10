using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class TestSageConnectionDtoValidator : AbstractValidator<TestSageConnectionDto>
{
    public TestSageConnectionDtoValidator()
        => ConnectionRules.Apply(this);
}
