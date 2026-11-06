using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

/// <summary>Contrat de <c>PlanningService</c>.</summary>
public interface IPlanningService
{
    Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default);

    /// <summary>
    /// Liste paginée du catalogue des shifts. Le catalogue reste petit, mais l'endpoint suit le
    /// même contrat que les autres listes (page bornée, total serveur) pour éviter un découpage
    /// côté client.
    /// </summary>
    Task<PagedResult<ShiftDto>> ListShiftsPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<ShiftDto> SaveShiftAsync(ShiftDto dto, CancellationToken ct = default);

    Task DeleteShiftAsync(decimal id, CancellationToken ct = default);

    Task<IReadOnlyList<PlanningEmployeeDto>> ListEmployeesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<EmployeePlanningDto>> ListEmployeePlanningAsync(DateTime from, DateTime to, int? cardId, CancellationToken ct = default);

    /// <summary>
    /// Liste paginée des lignes de planning. Le filtre date/carte est traduit en SQL ; les cartes
    /// (petite table) sont chargées une fois pour exposer matricule/nom/prénom.
    /// </summary>
    Task<PagedResult<EmployeePlanningDto>> ListEmployeePlanningPagedAsync(DateTime from, DateTime to, int? cardId, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    /// <summary>Complet (export) : salariés + nombre de jours planifiés sur la période.</summary>
    Task<IReadOnlyList<PlanningEmployeeStatDto>> ListEmployeeStatsAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Grille paginée des salariés avec leur nombre de jours planifiés.</summary>
    Task<PagedResult<PlanningEmployeeStatDto>> ListEmployeeStatsPagedAsync(DateTime from, DateTime to, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task SaveEmployeePlanningAsync(IReadOnlyCollection<EmployeePlanningDto> rows, CancellationToken ct = default);

    Task DeleteEmployeePlanningAsync(int id, CancellationToken ct = default);

    Task<ImportResultDto> ImportExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string> headers, CancellationToken ct = default);

    /// <summary>
    /// Génère le planning d'une semaine depuis un CSV « catégorie ; lundi ; (début ; fin) × 7 ».
    /// Chaque ligne alimente tous les salariés affectés à la catégorie, avec la pause de la catégorie.
    /// Portage de <c>GenererPlanningParCategorieAsync</c> du desktop.
    /// Écart assumé : un jour « OFF » est enregistré <c>OffP = true</c> avec des heures nulles, au lieu
    /// de la date sentinelle 1800-01-01 du desktop (le calcul de semaine ignore déjà ces jours).
    /// </summary>
    Task<ImportResultDto> GenerateFromCategoryCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default);

    /// <summary>
    /// Génère le planning d'une semaine depuis un CSV « matricule ; lundi ; (début ; pause ; fin) × 7 »,
    /// avec une pause saisie par jour. Portage de <c>GenererPlanningsAsync</c> du desktop.
    /// </summary>
    Task<ImportResultDto> GenerateFromMatriculeCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default);

    Task<ImportResultDto> ImportShiftsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);
}
