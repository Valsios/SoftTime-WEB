using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Base SAGE de paie cible : connexion, constantes et codes des cumuls d'heures supplémentaires.</summary>
public sealed record SagePayrollTarget(string Connection, ConstanteSage[] Constants, OvertimeSageCodes Codes);
