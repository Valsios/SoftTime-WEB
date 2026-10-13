using System.Globalization;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public sealed class ExternalSourceService : IExternalSourceService
{
    private readonly IUnitOfWork _uow;
    private readonly ISageContextFactory _sageFactory;
    private readonly IPointeuseContextFactory _pointeuseFactory;
    private readonly IMappedSourceReader _reader;

    public ExternalSourceService(
        IUnitOfWork uow,
        ISageContextFactory sageFactory,
        IPointeuseContextFactory pointeuseFactory,
        IMappedSourceReader reader)
    {
        _uow = uow;
        _sageFactory = sageFactory;
        _pointeuseFactory = pointeuseFactory;
        _reader = reader;
    }

    private static bool IsAutre(string? typeBase)
        => string.Equals(typeBase?.Trim(), "AUTRE", StringComparison.OrdinalIgnoreCase);

    private static string BuildSage(T_BDD_SAGE row)
        => ExternalConnectionFactory.Build(row.SERVEUR, row.NOM_BD, row.TYPE_AUTH == true, row.TLOGIN, row.TMDP);

    private static string BuildPointeuse(T_BDD_POINTEUSE row)
        => ExternalConnectionFactory.Build(row.SERVEUR, row.NOM_BD, row.TYPE_AUTH == true, row.TLOGIN, row.TMDP);

    private static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v is "1") return true;
        if (v is "0") return false;
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("oui", StringComparison.OrdinalIgnoreCase) || v.Equals("o", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("non", StringComparison.OrdinalIgnoreCase) || v.Equals("n", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static long? ParseLong(string? value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;
    private static short? ParseShort(string? value) => short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : null;

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private async Task<(string? Table, Dictionary<string, string> RoleToColumn)> LoadRoleMapAsync(bool isSage, int connectionId, string entityKind, CancellationToken ct)
    {
        var entities = isSage
            ? await _uow.Repository<T_SOURCE_ENTITY_MAPPING>().ListAsync(m => m.SageDbId == connectionId && m.EntityKind == entityKind, ct)
            : await _uow.Repository<T_SOURCE_ENTITY_MAPPING>().ListAsync(m => m.PointeuseDbId == connectionId && m.EntityKind == entityKind, ct);
        var entity = entities.FirstOrDefault()
            ?? throw new InvalidOperationException($"Aucun mapping '{entityKind}' configuré pour cette base.");
        var fields = await _uow.Repository<T_SOURCE_FIELD_MAPPING>().ListAsync(f => f.EntityMappingId == entity.Id, ct);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields)
            if (!string.IsNullOrWhiteSpace(f.SourceColumn))
                map[f.FieldRoleCode] = f.SourceColumn!.Trim();
        return (entity.SourceTable, map);
    }

    private async Task<IReadOnlyList<MappedRow>> ReadEntityAsync(bool isSage, int connectionId, string entityKind, string connectionString, MappedQuery? query, CancellationToken ct)
    {
        var (table, map) = await LoadRoleMapAsync(isSage, connectionId, entityKind, ct);
        if (string.IsNullOrWhiteSpace(table))
            return Array.Empty<MappedRow>();
        return await _reader.ReadAsync(connectionString, table!, map, query, ct);
    }

    public async Task<IReadOnlyList<ExternalEmployee>> GetEmployeesAsync(T_BDD_SAGE row, CancellationToken ct = default)
    {
        var conn = BuildSage(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var sage = _sageFactory.Create(conn);
            return sage.Employees.ToList()
                .Where(e => e.SalarieDesactive != 1)
                .Select(e => new ExternalEmployee(e.MatriculeSalarie.Trim(), e.SA_CompteurNumero, e.Nom, e.Prenom, e.NumeroDeBadge, false))
                .ToList();
        }
        var rows = await ReadEntityAsync(true, row.ID, "EMPLOYEE", conn, null, ct);
        var result = new List<ExternalEmployee>();
        foreach (var r in rows)
        {
            var matricule = r["SAGE_MATRICULE"];
            var id = ParseLong(r["SAGE_EMPLOYEE_ID"]);
            if (string.IsNullOrWhiteSpace(matricule) || id is null) continue;
            if (ParseBool(r["SAGE_INACTIVE_FLAG"]) == true) continue;
            result.Add(new ExternalEmployee(matricule!.Trim(), id.Value, r["SAGE_NOM"], r["SAGE_PRENOM"], r["SAGE_BADGE"], false));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExternalAffectation>> GetAffectationsAsync(T_BDD_SAGE row, CancellationToken ct = default)
    {
        var conn = BuildSage(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var sage = _sageFactory.Create(conn);
            return sage.Affectations.ToList()
                .Select(a => new ExternalAffectation(a.NumSalarie, a.Departement, a.Service, a.DateDebut, a.DateSortiePoste))
                .ToList();
        }
        var rows = await ReadEntityAsync(true, row.ID, "AFFECTATION", conn, null, ct);
        var result = new List<ExternalAffectation>();
        foreach (var r in rows)
        {
            var id = ParseLong(r["SAGE_AFF_EMPLOYEE_ID"]);
            if (id is null) continue;
            result.Add(new ExternalAffectation(id.Value, r["SAGE_AFF_DEPARTEMENT"], r["SAGE_AFF_SERVICE"], ParseDate(r["SAGE_AFF_START"]), ParseDate(r["SAGE_AFF_END"])));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExternalDepartment>> GetDepartmentsAsync(T_BDD_SAGE row, CancellationToken ct = default)
    {
        var conn = BuildSage(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var sage = _sageFactory.Create(conn);
            return sage.Departments.ToList()
                .Select(d => new ExternalDepartment(d.Code, d.Intitule))
                .ToList();
        }
        var rows = await ReadEntityAsync(true, row.ID, "DEPARTMENT", conn, null, ct);
        var result = new List<ExternalDepartment>();
        foreach (var r in rows)
        {
            var code = r["SAGE_DEPT_CODE"];
            if (string.IsNullOrWhiteSpace(code)) continue;
            result.Add(new ExternalDepartment(code!.Trim(), r["SAGE_DEPT_LABEL"]));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExternalHoliday>> GetCompanyCalendarAsync(T_BDD_SAGE row, CancellationToken ct = default)
    {
        var conn = BuildSage(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var sage = _sageFactory.Create(conn);
            return sage.CompanyCalendar.ToList()
                .Where(c => c.EtatJour == 1 && c.PeriodeDebut.HasValue)
                .Select(c => new ExternalHoliday(c.PeriodeDebut!.Value.Date))
                .ToList();
        }
        var rows = await ReadEntityAsync(true, row.ID, "COMPANY_CALENDAR", conn, null, ct);
        var result = new List<ExternalHoliday>();
        foreach (var r in rows)
        {
            var date = ParseDate(r["SAGE_CAL_DATE"]);
            if (date is null) continue;
            if (ParseBool(r["SAGE_CAL_IS_HOLIDAY"]) == false) continue;
            result.Add(new ExternalHoliday(date.Value.Date));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExternalConstant>> GetConstantsAsync(T_BDD_SAGE row, CancellationToken ct = default)
    {
        var conn = BuildSage(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var sage = _sageFactory.Create(conn);
            return sage.Constants.ToList()
                .Select(c => new ExternalConstant(c.CodeConstante, c.CodeOperande1, c.Intitule))
                .ToList();
        }
        var rows = await ReadEntityAsync(true, row.ID, "CONSTANT", conn, null, ct);
        return rows.Select(r => new ExternalConstant(r["SAGE_CST_CODE"], ParseShort(r["SAGE_CST_OPERANDE"]), r["SAGE_CST_LABEL"])).ToList();
    }

    public async Task<IReadOnlyList<ExternalEvent>> GetEventsAsync(T_BDD_SAGE row, CancellationToken ct = default)
    {
        var conn = BuildSage(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var sage = _sageFactory.Create(conn);
            return sage.Events.ToList()
                .Select(e => new ExternalEvent(e.CodeNE, e.Intitule))
                .ToList();
        }
        var rows = await ReadEntityAsync(true, row.ID, "ABSENCE_EVENT", conn, null, ct);
        return rows.Select(r => new ExternalEvent(r["SAGE_EVT_CODE"], r["SAGE_EVT_LABEL"])).ToList();
    }

    public async Task<IReadOnlyList<ExternalEmployeeEvent>> GetEmployeeEventsAsync(T_BDD_SAGE row, CancellationToken ct = default)
    {
        var conn = BuildSage(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var sage = _sageFactory.Create(conn);
            return sage.EmployeeEvents.ToList()
                .Select(e => new ExternalEmployeeEvent(
                    e.NumSalarie,
                    e.CodeNE,
                    e.PeriodeDebut,
                    e.PeriodeFin,
                    e.Matin.HasValue ? e.Matin.Value != 0 : null,
                    e.ApresMidi.HasValue ? e.ApresMidi.Value != 0 : null))
                .ToList();
        }
        var rows = await ReadEntityAsync(true, row.ID, "EMPLOYEE_EVENT", conn, null, ct);
        var result = new List<ExternalEmployeeEvent>();
        foreach (var r in rows)
        {
            var id = ParseLong(r["SAGE_EEV_EMPLOYEE_ID"]);
            if (id is null) continue;
            result.Add(new ExternalEmployeeEvent(id.Value, r["SAGE_EEV_CODE"], ParseDate(r["SAGE_EEV_START"]), ParseDate(r["SAGE_EEV_END"]), ParseBool(r["SAGE_EEV_MATIN"]), ParseBool(r["SAGE_EEV_APRESMIDI"])));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExternalPunchUser>> GetPunchUsersAsync(T_BDD_POINTEUSE row, CancellationToken ct = default)
    {
        var conn = BuildPointeuse(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var pte = _pointeuseFactory.Create(conn);
            return pte.Users.ToList()
                .Select(u => new ExternalPunchUser(u.USERID, u.BADGENUMBER, u.SSN, u.NAME))
                .ToList();
        }
        var rows = await ReadEntityAsync(false, row.ID, "PUNCH_USER", conn, null, ct);
        var result = new List<ExternalPunchUser>();
        foreach (var r in rows)
        {
            var id = ParseLong(r["PTE_USER_ID"]);
            if (id is null) continue;
            result.Add(new ExternalPunchUser(id.Value, r["PTE_USER_BADGE"], r["PTE_USER_SSN"], r["PTE_USER_NAME"]));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExternalPunch>> GetPunchesAsync(T_BDD_POINTEUSE row, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var conn = BuildPointeuse(row);
        if (!IsAutre(row.TYPE_BASE))
        {
            using var pte = _pointeuseFactory.Create(conn);
            return pte.CheckInOut.ToList()
                .Where(c => c.CHECKTIME >= from && c.CHECKTIME < to)
                .Select(c => new ExternalPunch(c.USERID, c.CHECKTIME, c.CHECKTYPE))
                .ToList();
        }
        var (table, map) = await LoadRoleMapAsync(false, row.ID, "PUNCH", ct);
        if (string.IsNullOrWhiteSpace(table))
            throw new InvalidOperationException("Table source non définie pour l'entité 'PUNCH'.");
        var dateColumn = map.TryGetValue("PTE_PUNCH_DATETIME", out var dc) ? dc : null;
        var query = dateColumn is null
            ? null
            : new MappedQuery(dateColumn, MappedFilterKind.Range, RangeFrom: from.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), RangeTo: to.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        var rows = await _reader.ReadAsync(conn, table!, map, query, ct);
        var result = new List<ExternalPunch>();
        foreach (var r in rows)
        {
            var id = ParseLong(r["PTE_PUNCH_USER_ID"]);
            var time = ParseDate(r["PTE_PUNCH_DATETIME"]);
            if (id is null || time is null) continue;
            result.Add(new ExternalPunch(id.Value, time.Value, r["PTE_PUNCH_TYPE"]));
        }
        return result;
    }
}
