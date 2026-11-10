using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.ExternalData;

/// <summary>
/// Accès unifié aux sources externes : chemin EF historique pour les bases STANDARD,
/// chemin piloté par le mapping (T_SOURCE_ENTITY_MAPPING / T_SOURCE_FIELD_MAPPING) pour les bases AUTRE.
/// </summary>
public interface IExternalSourceService
{
    Task<IReadOnlyList<ExternalEmployee>> GetEmployeesAsync(BddSage row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalAffectation>> GetAffectationsAsync(BddSage row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalDepartment>> GetDepartmentsAsync(BddSage row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalHoliday>> GetCompanyCalendarAsync(BddSage row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalConstant>> GetConstantsAsync(BddSage row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEvent>> GetEventsAsync(BddSage row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEmployeeEvent>> GetEmployeeEventsAsync(BddSage row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalPunchUser>> GetPunchUsersAsync(BddPointeuse row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalPunch>> GetPunchesAsync(BddPointeuse row, DateTime from, DateTime to, CancellationToken ct = default);
}
