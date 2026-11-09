using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Services.Employees;

public sealed class DepartementServiceLookup : IDepartementServiceLookup
{
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public DepartementServiceLookup(
        ITenantConnectionService tenant,
        IExternalSourceService externalSource)
    {
        _tenant = tenant;
        _externalSource = externalSource;
    }

    public async Task<DepartementServiceDto> GetDepartementServiceAsync(string matricule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(matricule))
            throw new DomainException("Le matricule est requis.");
        return await LookupFromSageAsync(matricule, ct);
    }

    private async Task<DepartementServiceDto> LookupFromSageAsync(string matricule, CancellationToken ct)
    {
        var mat = matricule.Trim();
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var emp = (await _externalSource.GetEmployeesAsync(sageRow, ct))
            .FirstOrDefault(e => string.Equals(e.Matricule, mat, StringComparison.OrdinalIgnoreCase));
        if (emp == null)
            return new DepartementServiceDto(matricule, null, null);
        var affs = (await _externalSource.GetAffectationsAsync(sageRow, ct))
            .Where(a => a.EmployeeId == emp.EmployeeId)
            .ToList();
        var current = PickCurrentAffectation(affs);
        return new DepartementServiceDto(matricule, current?.Departement?.Trim(), current?.Service?.Trim());
    }

    private static HistoriqueAffectationSage? PickCurrentAffectation(IEnumerable<HistoriqueAffectationSage> rows)
    {
        var list = rows.ToList();
        return list.Where(a => a.DateSortiePoste == null).OrderByDescending(a => a.DateDebut).FirstOrDefault()
               ?? list.OrderByDescending(a => a.DateDebut).FirstOrDefault();
    }

    private static ExternalAffectation? PickCurrentAffectation(IEnumerable<ExternalAffectation> rows)
    {
        var list = rows.ToList();
        return list.Where(a => a.DateSortie == null).OrderByDescending(a => a.DateDebut).FirstOrDefault()
               ?? list.OrderByDescending(a => a.DateDebut).FirstOrDefault();
    }

    public async Task<IReadOnlyList<DepartementServiceDto>> GetDepartementServiceBatchAsync(
    IEnumerable<string> matricules, CancellationToken ct = default)
    {
        var list = Normaliser(matricules);
        if (list.Count == 0)
            return Array.Empty<DepartementServiceDto>();

        var sageRow = await _tenant.GetSageRowAsync(ct);
        var matSet = list.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var emps = (await _externalSource.GetEmployeesAsync(sageRow, ct))
            .Where(e => matSet.Contains(e.Matricule))
            .ToList();
        var idSet = emps.Select(e => e.EmployeeId).ToHashSet();
        var byNum = (await _externalSource.GetAffectationsAsync(sageRow, ct))
            .Where(a => idSet.Contains(a.EmployeeId))
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // La source externe peut contenir des matricules en double : on conserve le premier
        // au lieu de lever une exception de cle dupliquee.
        var numByMat = NumerosParMatricule(emps);
        return list.Select(m =>
        {
            var cur = numByMat.TryGetValue(m, out var num) && byNum.TryGetValue(num, out var rows)
                ? PickCurrentAffectation(rows) : null;
            return new DepartementServiceDto(m, cur?.Departement?.Trim(), cur?.Service?.Trim());
        }).ToList();
    }

    /// <summary>Matricules renseignés, nettoyés et dédoublonnés (2 000 au plus).</summary>
    private static List<string> Normaliser(IEnumerable<string> matricules)
    {
        var list = matricules
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct()
            .Take(2000)
            .ToList();
        return list;
    }

    /// <summary>La source externe peut contenir des matricules en double : le premier est conservé.</summary>
    private static Dictionary<string, long> NumerosParMatricule(List<ExternalEmployee> emps)
    {
        var numByMat = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in emps)
            numByMat.TryAdd(e.Matricule, e.EmployeeId);
        return numByMat;
    }
}
