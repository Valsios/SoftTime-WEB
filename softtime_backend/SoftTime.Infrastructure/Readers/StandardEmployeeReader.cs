using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
 
namespace SoftTime.Infrastructure.Readers;
 
public class StandardEmployeeReader : IEmployeeReader
{
    private readonly ISageContextFactory _sageFactory;
    public StandardEmployeeReader(ISageContextFactory sageFactory) => _sageFactory = sageFactory;
 
    public bool CanHandle(T_BDD_SAGE sageRow) => sageRow.TYPE_BASE == "STANDARD";
 
    public async Task<IReadOnlyList<NormalizedEmployee>> ListActiveEmployeesAsync(T_BDD_SAGE sageRow, CancellationToken ct)
    {
        using var sage = _sageFactory.Create(ExternalConnectionFactory.Build(
            sageRow.SERVEUR, sageRow.NOM_BD, sageRow.TYPE_AUTH == true, sageRow.TLOGIN, sageRow.TMDP));
        return sage.Employees
            .Where(e => e.SalarieDesactive != 1)
            .ToList()
            .Select(e => new NormalizedEmployee(
                Matricule: e.MatriculeSalarie.Trim(),
                Nom: e.Nom,
                Prenom: e.Prenom,
                NumeroBadge: e.NumeroDeBadge,
                Desactive: e.SalarieDesactive == 1,
                CleInterne: e.SA_CompteurNumero.ToString()))
            .ToList();
    }
}