using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class TestSageConnectionDtoValidator : AbstractValidator<TestSageConnectionDto>
{
    public TestSageConnectionDtoValidator()
        => ConnectionRules.Apply(this, x => x.Serveur, x => x.Login, x => x.Password, x => x.NomBd, x => x.TypeBase);
}
