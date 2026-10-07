using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Services;

public class EmployeeReaderResolver
{
    private readonly IEnumerable<IEmployeeReader> _readers;
    public EmployeeReaderResolver(IEnumerable<IEmployeeReader> readers) => _readers = readers;

    public IEmployeeReader Resolve(T_BDD_SAGE sageRow)
        => _readers.FirstOrDefault(r => r.CanHandle(sageRow))
           ?? throw new InvalidOperationException($"Aucun lecteur employé ne gère le type de base '{sageRow.TYPE_BASE}'.");
}