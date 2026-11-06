using FluentValidation;

using SoftTime.Application.DTOs;

namespace SoftTime.Application.Validators;

public class PointeuseDbDtoValidator : AbstractValidator<PointeuseDbDto>
{
    public PointeuseDbDtoValidator()
    {
        ConnectionRules.Apply(this, x => x.Serveur, x => x.Login, x => x.Password, x => x.NomBd, x => x.TypeBase);
        RuleFor(x => x.TypePointage).MaximumLength(50).WithMessage("Le type de pointage ne peut pas dépasser 50 caractères.");
    }
}
