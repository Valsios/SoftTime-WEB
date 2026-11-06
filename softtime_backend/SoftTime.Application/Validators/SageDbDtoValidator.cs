using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class SageDbDtoValidator : AbstractValidator<SageDbDto>
{
    public SageDbDtoValidator()
        => ConnectionRules.Apply(this, x => x.Serveur, x => x.Login, x => x.Password, x => x.NomBd, x => x.TypeBase);
}
