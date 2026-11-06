namespace SoftTime.Application.Abstractions;

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
public interface ISageLeaveCalendar
{
    /// <summary>Congés de tous les salariés du tenant, par clé canonique de matricule.</summary>
    Task<Dictionary<string, List<LeaveDay>>> LoadAsync(CancellationToken ct = default);
}
