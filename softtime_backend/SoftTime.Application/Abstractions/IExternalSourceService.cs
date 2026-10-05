using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

public sealed record ExternalEmployee(string Matricule, long EmployeeId, string? Nom, string? Prenom, string? Badge, bool Inactive);

public sealed record ExternalAffectation(long EmployeeId, string? Departement, string? Service, DateTime? DateDebut, DateTime? DateSortie);

public sealed record ExternalDepartment(string Code, string? Label);

public sealed record ExternalHoliday(DateTime Date);

public sealed record ExternalConstant(string? Code, short? Operande, string? Label);

public sealed record ExternalEvent(string? Code, string? Label);

public sealed record ExternalEmployeeEvent(long EmployeeId, string? Code, DateTime? Start, DateTime? End, bool? Matin, bool? ApresMidi);

public sealed record ExternalPunchUser(long UserId, string? Badge, string? Ssn, string? Name);

public sealed record ExternalPunch(long UserId, DateTime CheckTime, string? CheckType);

/// <summary>
/// Accès unifié aux sources externes : chemin EF historique pour les bases STANDARD,
/// chemin piloté par le mapping (T_SOURCE_ENTITY_MAPPING / T_SOURCE_FIELD_MAPPING) pour les bases AUTRE.
/// </summary>
public interface IExternalSourceService
{
    Task<IReadOnlyList<ExternalEmployee>> GetEmployeesAsync(T_BDD_SAGE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalAffectation>> GetAffectationsAsync(T_BDD_SAGE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalDepartment>> GetDepartmentsAsync(T_BDD_SAGE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalHoliday>> GetCompanyCalendarAsync(T_BDD_SAGE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalConstant>> GetConstantsAsync(T_BDD_SAGE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEvent>> GetEventsAsync(T_BDD_SAGE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEmployeeEvent>> GetEmployeeEventsAsync(T_BDD_SAGE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalPunchUser>> GetPunchUsersAsync(T_BDD_POINTEUSE row, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalPunch>> GetPunchesAsync(T_BDD_POINTEUSE row, DateTime from, DateTime to, CancellationToken ct = default);
}
