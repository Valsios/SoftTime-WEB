using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IEmployeePlanningService
{
    Task<IReadOnlyList<PlanningEmployeeDto>> ListEmployeesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<EmployeePlanningDto>> ListEmployeePlanningAsync(DateTime from, DateTime to, int? cardId, CancellationToken ct = default);

    /// <summary>
    /// Liste paginée des lignes de planning. Le filtre date/carte est traduit en SQL ; les cartes
    /// (petite table) sont chargées une fois pour exposer matricule/nom/prénom.
    /// </summary>
    Task<PagedResult<EmployeePlanningDto>> ListEmployeePlanningPagedAsync(EmployeePlanningQuery query, CancellationToken ct = default);

    /// <summary>Complet (export) : salariés + nombre de jours planifiés sur la période.</summary>
    Task<IReadOnlyList<PlanningEmployeeStatDto>> ListEmployeeStatsAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Grille paginée des salariés avec leur nombre de jours planifiés.</summary>
    Task<PagedResult<PlanningEmployeeStatDto>> ListEmployeeStatsPagedAsync(EmployeePlanningQuery query, CancellationToken ct = default);

    Task SaveEmployeePlanningAsync(IReadOnlyCollection<EmployeePlanningDto> rows, CancellationToken ct = default);

    Task DeleteEmployeePlanningAsync(int id, CancellationToken ct = default);
}
