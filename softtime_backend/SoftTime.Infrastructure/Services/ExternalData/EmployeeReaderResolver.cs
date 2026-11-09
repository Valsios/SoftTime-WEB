using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.ExternalData;

public class EmployeeReaderResolver : IEmployeeReaderResolver
{
    private readonly IEnumerable<IEmployeeReader> _readers;
    public EmployeeReaderResolver(IEnumerable<IEmployeeReader> readers) => _readers = readers;

    public IEmployeeReader Resolve(BddSage sageRow)
        => _readers.FirstOrDefault(r => r.CanHandle(sageRow))
           ?? throw new DomainException($"Aucun lecteur employé ne gère le type de base '{sageRow.TypeBase}'.");
}
