using FluentValidation;

using SoftTime.Application.DTOs;
using SoftTime.Application.Services;

namespace SoftTime.Application.Validators;

public class ClosingAnalysisQueryValidator : AbstractValidator<ClosingAnalysisQuery>
{
    public const string TriGravite = "gravite";

    /// <summary>Longueur maximale du filtre matricule (un matricule SAGE est bien plus court).</summary>
    public const int MatriculeMaxLength = 50;

    public ClosingAnalysisQueryValidator()
    {
        RuleFor(x => x.Gravites)
            .Must(g => ClosingSeverityFilter.TryParse(g, out _))
            .WithMessage(ClosingSeverityFilter.Message);
        RuleFor(x => x.Types)
            .Must(t => ClosingTypeFilter.TryParse(t, out _))
            .WithMessage(ClosingTypeFilter.Message);
        RuleFor(x => x.Matricule)
            .MaximumLength(MatriculeMaxLength)
            .WithMessage($"Le paramètre matricule ne peut pas dépasser {MatriculeMaxLength} caractères.");
        RuleFor(x => x.Tri)
            .Must(t => string.IsNullOrWhiteSpace(t)
                       || string.Equals(t.Trim(), TriGravite, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"Le paramètre tri n'accepte que la valeur « {TriGravite} ».");
    }
}
