using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
 
namespace SoftTime.Infrastructure.Readers;
 
public class StandardEmployeeReader : IEmployeeReader
{
    private readonly ISageContextFactory _sageFactory;
    public StandardEmployeeReader(ISageContextFactory sageFactory) => _sageFactory = sageFactory;
 
    public bool CanHandle(BddSage sageRow) => sageRow.TypeBase == "STANDARD";
 
    public async Task<IReadOnlyList<NormalizedEmployee>> ListActiveEmployeesAsync(BddSage sageRow, CancellationToken ct)
    {
        using var sage = _sageFactory.Create(ExternalConnectionFactory.Build(
            SqlServerCredentials.Of(sageRow),
            sageRow.NomBd));
        return sage.Employees
            .Where(e => e.SalarieDesactive != 1)
            .ToList()
            .Select(e => new NormalizedEmployee(
                Matricule: e.MatriculeSalarie.Trim(),
                Nom: e.Nom,
                Prenom: e.Prenom,
                NumeroBadge: e.NumeroDeBadge,
                Desactive: e.SalarieDesactive == 1,
                CleInterne: e.SaCompteurNumero.ToString()))
            .ToList();
    }
}