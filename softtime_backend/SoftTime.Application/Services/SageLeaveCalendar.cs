using SoftTime.Application.Abstractions;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Congés SAGE du tenant courant, indexés par clé canonique de matricule (les zéros de remplissage
/// diffèrent entre les deux bases). Chargé en une seule passe : le desktop mettait les congés en
/// cache par matricule.
///
/// Source (base de type <c>STANDARD</c>) :
/// <list type="bullet">
///   <item><c>T_GHR</c> : catalogue des codes d'événement (tout sauf <c>0200</c> / <c>0300</c>) ;</item>
///   <item><c>T_SAL</c> : correspondance <c>SA_CompteurNumero</c> → matricule ;</item>
///   <item><c>T_GHRSAL</c> : périodes d'événement du salarié (<c>PeriodeDebut</c> / <c>PeriodeFin</c>).</item>
/// </list>
/// Pour une base de type <c>AUTRE</c>, les mêmes informations viennent des mappings dynamiques
/// (<c>ABSENCE_EVENT</c> / <c>EMPLOYEE_EVENT</c>) via <see cref="IExternalSourceService"/>.
///
/// Partagé par la validation semaine (<see cref="OvertimeService"/>) et le tableau de bord
/// analytique (<see cref="DashboardService"/>) afin que les deux écrans comptent les mêmes journées.
/// </summary>
public sealed class SageLeaveCalendar
{
    private readonly TenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public SageLeaveCalendar(TenantConnectionService tenant, IExternalSourceService externalSource)
    {
        _tenant = tenant;
        _externalSource = externalSource;
    }

    /// <summary>Congés de tous les salariés du tenant, par clé canonique de matricule.</summary>
    public async Task<Dictionary<string, List<LeaveDay>>> LoadAsync(CancellationToken ct = default)
    {
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var codes = (await _externalSource.GetEventsAsync(sageRow, ct))
            .Select(e => e.Code)
            .Where(LeaveRules.EstCodeConge)
            .Select(c => c!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parCle = new Dictionary<string, List<LeaveDay>>(StringComparer.Ordinal);
        if (codes.Count == 0)
            return parCle;

        var matriculeParNumSalarie = (await _externalSource.GetEmployeesAsync(sageRow, ct))
            .Where(e => !string.IsNullOrEmpty(e.Matricule))
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => MatriculeIdentity.Cle(g.First().Matricule));

        return (await _externalSource.GetEmployeeEventsAsync(sageRow, ct))
            .Where(e => e.Code is not null && codes.Contains(e.Code.Trim())
                        && e.Start.HasValue && e.End.HasValue
                        && matriculeParNumSalarie.ContainsKey(e.EmployeeId))
            .GroupBy(e => matriculeParNumSalarie[e.EmployeeId])
            .ToDictionary(
                g => g.Key,
                g => LeaveRules.Expandre(g.Select(e =>
                    new LeavePeriod(e.Start!.Value, e.End!.Value, e.Matin == true, e.ApresMidi == true, e.Code))).ToList());
    }
}
