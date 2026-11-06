using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

/// <summary>Contrat de <c>PayrollWriterResolver</c>.</summary>
public interface IPayrollWriterResolver
{
    IPayrollWriter Resolve(BddSage database);
}
