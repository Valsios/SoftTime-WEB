using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.ExternalData;

/// <summary>Contrat de <c>EmployeeReaderResolver</c>.</summary>
public interface IEmployeeReaderResolver
{
    IEmployeeReader Resolve(BddSage sageRow);
}
