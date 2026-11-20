using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Base SAGE de paie cible : connexion, constantes et codes des cumuls d'heures supplémentaires.</summary>
public sealed record SagePayrollTarget(string Connection, ConstanteSage[] Constants, OvertimeSageCodes Codes)
{
    /// <summary>Variables envoyées à la paie ; par défaut les sept variables d'heures supplémentaires (historique).</summary>
    public PayrollVariableSelection Variables { get; init; } = PayrollVariableSelection.Default;

    /// <summary>
    /// Code de paie d'une variable : celui du catalogue commun des codes pour une variable qui y figure, sinon le code
    /// choisi dans la configuration de la base. Une variable sans code est refusée.
    /// </summary>
    public string CodeDe(string variable)
    {
        var definition = PayrollVariableCatalog.Definition(variable);
        if (definition.CodeCommun)
            return Codes.CodePour(definition.Id);
        return Variables.CodeDe(definition.Id)
            ?? throw new DomainException($"Aucun code de paie n'est choisi pour la variable {definition.Libelle}.");
    }
}
