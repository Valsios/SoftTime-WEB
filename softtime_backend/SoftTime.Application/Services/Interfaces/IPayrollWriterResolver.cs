using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="PayrollWriterResolver"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IPayrollWriterResolver
{
    IPayrollWriter Resolve(T_BDD_SAGE database);
}
