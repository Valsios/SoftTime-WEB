using System.Text.RegularExpressions;
using AutoMapper;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Abstractions;

namespace SoftTime.Application.Services;

public class CatalogService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly TenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly IExternalDiscoveryService _discovery;

    public CatalogService(IUnitOfWork uow, IMapper mapper, TenantConnectionService tenant, IExternalSourceService externalSource, IExternalDiscoveryService discovery)
    {
        _uow = uow;
        _mapper = mapper;
        _tenant = tenant;
        _externalSource = externalSource;
        _discovery = discovery;
    }

    public async Task<IReadOnlyList<SageDbDto>> ListSageAsync(CancellationToken ct = default)
    {
        await EnsureMappingSchemaAsync(ct);
        await MigrateLegacyMappingsAsync(ct);
        var rows = await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, ct);
        var byConnection = await LoadMappingLookupAsync(true, ct);
        return rows.Select(r => _mapper.Map<SageDbDto>(r) with
        {
            Mappings = byConnection.TryGetValue(r.ID, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
    }

    public async Task<SageDbDto> SaveSageAsync(SageDbDto dto, CancellationToken ct = default)
    {
        if (dto.TypeBase != "STANDARD" && dto.TypeBase != "AUTRE")
            throw new InvalidOperationException("TypeBase doit valoir 'STANDARD' ou 'AUTRE'.");
        if (dto.TypeBase == "AUTRE" && (dto.Mappings is null || dto.Mappings.Count == 0))
            throw new InvalidOperationException("Une base 'Autre' nécessite au minimum un mapping de source.");
        await EnsureMappingSchemaAsync(ct);
        var repo = _uow.Repository<T_BDD_SAGE>();
        var doublon = (await repo.ListAsync(
            s => s.ID != dto.Id
                 && s.SERVEUR == dto.Serveur
                 && s.NOM_BD == dto.NomBd, ct)).Any();
        if (doublon)
            throw new InvalidOperationException("Cette base (serveur + nom) est déjà enregistrée.");
        T_BDD_SAGE entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<T_BDD_SAGE>(dto);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.SERVEUR = dto.Serveur;
            entity.TLOGIN = dto.Login;
            entity.TMDP = dto.Password;
            entity.TYPE_AUTH = dto.SqlAuth;
            entity.NOM_BD = dto.NomBd;
            entity.TYPE_BASE = dto.TypeBase;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        if (dto.Mappings is not null)
        {
            await ValidateMappingsAsync(true, dto.Mappings, ct);
            await ReplaceMappingsAsync(true, entity.ID, dto.Mappings, ct);
        }
        var result = _mapper.Map<SageDbDto>(entity);
        return result with { Mappings = await LoadMappingsForAsync(true, entity.ID, ct) };
    }
    // Verifie la connexion et, en mode STANDARD, la presence des tables attendues par le reste de l'application.
    private static readonly string[] SageStandardTables = { "T_SAL", "T_HST_AFFECTATION", "T_DEPARTEMENT", "T_GHRCAL_SOCIETE", "T_CST" };
    public async Task<ConnectionTestResultDto> TestSageConnectionAsync(TestSageConnectionDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Serveur) || string.IsNullOrWhiteSpace(dto.NomBd))
            return new ConnectionTestResultDto(false, "Serveur et nom de base requis.");
        IReadOnlyList<string> tables;
        try
        {
            tables = await _discovery.ListTablesAsync(dto.Serveur, dto.NomBd, dto.SqlAuth ?? true, dto.Login, dto.Password, ct);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResultDto(false, $"Connexion impossible : {ex.Message}");
        }
        if (dto.TypeBase == "AUTRE")
        {
            var missingCustom = new List<string>();
            if (dto.Mappings is { Count: > 0 })
            {
                foreach (var t in dto.Mappings.Where(x => !string.IsNullOrWhiteSpace(x.SourceTable)).Select(x => x.SourceTable!))
                    if (!tables.Contains(t, StringComparer.OrdinalIgnoreCase) && !missingCustom.Contains(t, StringComparer.OrdinalIgnoreCase))
                        missingCustom.Add(t);
                if (missingCustom.Count == 0)
                {
                    var missingCols = await ValidateMappingColumnsAsync(dto.Serveur!, dto.NomBd!, dto.SqlAuth ?? true, dto.Login, dto.Password, dto.Mappings, ct);
                    if (missingCols.Count > 0)
                        return new ConnectionTestResultDto(false, "Connexion réussie mais certaines colonnes mappées sont introuvables.", missingCols);
                }
            }
            return missingCustom.Count == 0
                ? new ConnectionTestResultDto(true, "Connexion réussie.")
                : new ConnectionTestResultDto(false, "Connexion réussie mais certaines tables choisies sont introuvables.", missingCustom);
        }
        var missing = SageStandardTables.Where(t => !tables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        return missing.Count == 0
            ? new ConnectionTestResultDto(true, "Connexion réussie, structure Sage standard reconnue.")
            : new ConnectionTestResultDto(false, "Connexion réussie mais certaines tables Sage attendues sont introuvables.", missing);
    }

    public async Task DeleteSageAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_BDD_SAGE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PointeuseDbDto>> ListPointeuseAsync(CancellationToken ct = default)
    {
        await EnsureMappingSchemaAsync(ct);
        await MigrateLegacyMappingsAsync(ct);
        var rows = await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, ct);
        var byConnection = await LoadMappingLookupAsync(false, ct);
        return rows.Select(r => _mapper.Map<PointeuseDbDto>(r) with
        {
            Mappings = byConnection.TryGetValue(r.ID, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
    }

    public async Task<PointeuseDbDto> SavePointeuseAsync(PointeuseDbDto dto, CancellationToken ct = default)
    {
        if (dto.TypeBase != "STANDARD" && dto.TypeBase != "AUTRE")
            throw new InvalidOperationException("TypeBase doit valoir 'STANDARD' ou 'AUTRE'.");
        if (dto.TypeBase == "AUTRE" && (dto.Mappings is null || dto.Mappings.Count == 0))
            throw new InvalidOperationException("Une base 'Autre' nécessite au minimum un mapping de source.");
        await EnsureMappingSchemaAsync(ct);
        var repo = _uow.Repository<T_BDD_POINTEUSE>();
        var doublon = (await repo.ListAsync(
            p => p.ID != dto.Id
                 && p.SERVEUR == dto.Serveur
                 && p.NOM_BD == dto.NomBd, ct)).Any();
        if (doublon)
            throw new InvalidOperationException("Cette base (serveur + nom) est déjà enregistrée.");
        if (dto.Active == true)
        {
            var all = await repo.ListAsync(_ => true, ct);
            foreach (var p in all)
            {
                p.ACTIVE = p.ID == dto.Id;
                repo.Update(p);
            }
        }
        T_BDD_POINTEUSE entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<T_BDD_POINTEUSE>(dto);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.SERVEUR = dto.Serveur;
            entity.TLOGIN = dto.Login;
            entity.TMDP = dto.Password;
            entity.TYPE_AUTH = dto.SqlAuth;
            entity.NOM_BD = dto.NomBd;
            entity.TYPE_POINTAGE = dto.TypePointage;
            entity.ACTIVE = dto.Active;
            entity.TYPE_BASE = dto.TypeBase;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        if (dto.Mappings is not null)
        {
            await ValidateMappingsAsync(false, dto.Mappings, ct);
            await ReplaceMappingsAsync(false, entity.ID, dto.Mappings, ct);
        }
        var result = _mapper.Map<PointeuseDbDto>(entity);
        return result with { Mappings = await LoadMappingsForAsync(false, entity.ID, ct) };
    }
    private static readonly string[] PointeuseStandardTables = { "USERINFO", "CHECKINOUT" };
    public async Task<ConnectionTestResultDto> TestPointeuseConnectionAsync(TestPointeuseConnectionDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Serveur) || string.IsNullOrWhiteSpace(dto.NomBd))
            return new ConnectionTestResultDto(false, "Serveur et nom de base requis.");
        IReadOnlyList<string> tables;
        try
        {
            tables = await _discovery.ListTablesAsync(dto.Serveur, dto.NomBd, dto.SqlAuth ?? true, dto.Login, dto.Password, ct);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResultDto(false, $"Connexion impossible : {ex.Message}");
        }
        if (dto.TypeBase == "AUTRE")
        {
            var missingCustom = new List<string>();
            if (dto.Mappings is { Count: > 0 })
            {
                foreach (var t in dto.Mappings.Where(x => !string.IsNullOrWhiteSpace(x.SourceTable)).Select(x => x.SourceTable!))
                    if (!tables.Contains(t, StringComparer.OrdinalIgnoreCase) && !missingCustom.Contains(t, StringComparer.OrdinalIgnoreCase))
                        missingCustom.Add(t);
                if (missingCustom.Count == 0)
                {
                    var missingCols = await ValidateMappingColumnsAsync(dto.Serveur!, dto.NomBd!, dto.SqlAuth ?? true, dto.Login, dto.Password, dto.Mappings, ct);
                    if (missingCols.Count > 0)
                        return new ConnectionTestResultDto(false, "Connexion réussie mais certaines colonnes mappées sont introuvables.", missingCols);
                }
            }
            return missingCustom.Count == 0
                ? new ConnectionTestResultDto(true, "Connexion réussie.")
                : new ConnectionTestResultDto(false, "Connexion réussie mais certaines tables choisies sont introuvables.", missingCustom);
        }
        var missing = PointeuseStandardTables.Where(t => !tables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        return missing.Count == 0
            ? new ConnectionTestResultDto(true, "Connexion réussie, structure pointeuse standard reconnue.")
            : new ConnectionTestResultDto(false, "Connexion réussie mais les tables USERINFO/CHECKINOUT sont introuvables.", missing);
    }

    public async Task DeletePointeuseAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_BDD_POINTEUSE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ClockParamDto> GetClockAsync(CancellationToken ct = default)
    {
        var row = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault();
        return row == null ? new ClockParamDto(0, false) : new ClockParamDto(row.IDPOINT, row.MULTIPOINT);
    }

    public async Task<ClockParamDto> SaveClockAsync(ClockParamDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CLOCK>();
        var row = (await repo.ListAsync(_ => true, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new T_CLOCK { MULTIPOINT = dto.MultiPoint };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.MULTIPOINT = dto.MultiPoint;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        return new ClockParamDto(row.IDPOINT, row.MULTIPOINT);
    }

    public async Task<DepartementServiceDto> GetDepartementServiceAsync(string matricule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(matricule))
            throw new InvalidOperationException("Le matricule est requis.");
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

    private static T_HST_AFFECTATION? PickCurrentAffectation(IEnumerable<T_HST_AFFECTATION> rows)
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
        var list = matricules
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct()
            .Take(2000)
            .ToList();
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
        var numByMat = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in emps)
            numByMat.TryAdd(e.Matricule, e.EmployeeId);
        return list.Select(m =>
        {
            var cur = numByMat.TryGetValue(m, out var num) && byNum.TryGetValue(num, out var rows)
                ? PickCurrentAffectation(rows) : null;
            return new DepartementServiceDto(m, cur?.Departement?.Trim(), cur?.Service?.Trim());
        }).ToList();
    }

    public Task<IReadOnlyList<string>> DiscoverServersAsync(CancellationToken ct = default)
        => _discovery.ListServersAsync(ct);

    public Task<IReadOnlyList<string>> DiscoverDatabasesAsync(DiscoverDatabasesDto dto, CancellationToken ct = default)
        => _discovery.ListDatabasesAsync(dto.Serveur, dto.SqlAuth, dto.Login, dto.Password, ct);

    public Task<IReadOnlyList<string>> DiscoverTablesAsync(DiscoverTablesDto dto, CancellationToken ct = default)
        => _discovery.ListTablesAsync(dto.Serveur, dto.Base, dto.SqlAuth, dto.Login, dto.Password, ct);

    public Task<IReadOnlyList<string>> DiscoverColumnsAsync(DiscoverColumnsDto dto, CancellationToken ct = default)
        => _discovery.ListColumnsAsync(dto.Serveur, dto.Base, dto.Table, dto.SqlAuth, dto.Login, dto.Password, ct);

    // Cree (idempotent) le modele de mapping dynamique et seed le catalogue des roles.
    private async Task EnsureMappingSchemaAsync(CancellationToken ct)
    {
        await _uow.ExecuteSqlAsync("""
            IF OBJECT_ID(N'dbo.T_FIELD_ROLE', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_FIELD_ROLE (
                    Code nvarchar(64) NOT NULL PRIMARY KEY,
                    Label nvarchar(128) NULL,
                    SystemType nvarchar(16) NOT NULL,
                    EntityKind nvarchar(32) NOT NULL,
                    IsRequired bit NOT NULL CONSTRAINT DF_T_FIELD_ROLE_IsRequired DEFAULT 0,
                    AutoMappingPatterns nvarchar(256) NULL,
                    SortOrder int NOT NULL CONSTRAINT DF_T_FIELD_ROLE_SortOrder DEFAULT 0
                );
            END

            IF OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_SOURCE_ENTITY_MAPPING (
                    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    SystemType nvarchar(16) NOT NULL,
                    SageDbId int NULL,
                    PointeuseDbId int NULL,
                    EntityKind nvarchar(32) NOT NULL,
                    SourceTable nvarchar(128) NULL
                );
            END

            IF OBJECT_ID(N'dbo.T_SOURCE_FIELD_MAPPING', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_SOURCE_FIELD_MAPPING (
                    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EntityMappingId int NOT NULL,
                    FieldRoleCode nvarchar(64) NOT NULL,
                    SourceColumn nvarchar(128) NULL
                );
            END

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (SageDbId, EntityKind);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (PointeuseDbId, EntityKind);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SFM_Entity_Role' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_FIELD_MAPPING'))
                CREATE UNIQUE INDEX UX_SFM_Entity_Role ON dbo.T_SOURCE_FIELD_MAPPING (EntityMappingId, FieldRoleCode);

            INSERT INTO dbo.T_FIELD_ROLE (Code, Label, SystemType, EntityKind, IsRequired, AutoMappingPatterns, SortOrder)
            SELECT v.Code, v.Label, v.SystemType, v.EntityKind, v.IsRequired, v.AutoMappingPatterns, v.SortOrder
            FROM (VALUES
                (N'SAGE_MATRICULE', N'Matricule', N'SAGE', N'EMPLOYEE', 1, N'matricule|matric|mat', 10),
                (N'SAGE_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'EMPLOYEE', 1, N'compteur|numsalarie|num_salarie|salarieid', 20),
                (N'SAGE_NOM', N'Nom', N'SAGE', N'EMPLOYEE', 0, N'nom|name', 30),
                (N'SAGE_PRENOM', N'Prénom', N'SAGE', N'EMPLOYEE', 0, N'prenom|prénom|firstname', 40),
                (N'SAGE_BADGE', N'Numéro de badge', N'SAGE', N'EMPLOYEE', 0, N'badge|badgenumber|numbadge|num_badge|numerobadge', 50),
                (N'SAGE_INACTIVE_FLAG', N'Indicateur désactivé', N'SAGE', N'EMPLOYEE', 0, N'desactive|désactivé|inactive|actif', 60),
                (N'SAGE_AFF_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'AFFECTATION', 1, N'numsalarie|num_salarie|compteur|salarieid', 10),
                (N'SAGE_AFF_DEPARTEMENT', N'Code département', N'SAGE', N'AFFECTATION', 0, N'departement|département|code_dep|dep', 20),
                (N'SAGE_AFF_SERVICE', N'Service', N'SAGE', N'AFFECTATION', 0, N'service', 30),
                (N'SAGE_AFF_START', N'Date début', N'SAGE', N'AFFECTATION', 0, N'datedebut|date_debut|debut|startdate', 40),
                (N'SAGE_AFF_END', N'Date sortie', N'SAGE', N'AFFECTATION', 0, N'datesortie|date_sortie|datefin|fin|enddate', 50),
                (N'SAGE_DEPT_CODE', N'Code département', N'SAGE', N'DEPARTMENT', 1, N'code|code_dep|code_departement', 10),
                (N'SAGE_DEPT_LABEL', N'Intitulé département', N'SAGE', N'DEPARTMENT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
                (N'SAGE_CAL_DATE', N'Date', N'SAGE', N'COMPANY_CALENDAR', 1, N'periode|date|jour', 10),
                (N'SAGE_CAL_IS_HOLIDAY', N'Jour férié', N'SAGE', N'COMPANY_CALENDAR', 0, N'etat|etatjour|ferie|férié|holiday', 20),
                (N'SAGE_CST_CODE', N'Code constante', N'SAGE', N'CONSTANT', 1, N'code|codeconstante|code_constante', 10),
                (N'SAGE_CST_LABEL', N'Intitulé', N'SAGE', N'CONSTANT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
                (N'SAGE_CST_OPERANDE', N'Opérande', N'SAGE', N'CONSTANT', 0, N'operande|codeoperande|ordre|noordre', 30),
                (N'SAGE_EVT_CODE', N'Code événement', N'SAGE', N'ABSENCE_EVENT', 0, N'code|codene|code_ne', 10),
                (N'SAGE_EVT_LABEL', N'Intitulé', N'SAGE', N'ABSENCE_EVENT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
                (N'SAGE_EEV_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'EMPLOYEE_EVENT', 1, N'numsalarie|num_salarie|compteur|salarieid', 10),
                (N'SAGE_EEV_CODE', N'Code événement', N'SAGE', N'EMPLOYEE_EVENT', 0, N'code|codene|code_ne', 20),
                (N'SAGE_EEV_START', N'Début période', N'SAGE', N'EMPLOYEE_EVENT', 0, N'periode|debut|start|datedebut', 30),
                (N'SAGE_EEV_END', N'Fin période', N'SAGE', N'EMPLOYEE_EVENT', 0, N'periode|fin|end|datefin', 40),
                (N'SAGE_EEV_MATIN', N'Matin', N'SAGE', N'EMPLOYEE_EVENT', 0, N'matin|am', 50),
                (N'SAGE_EEV_APRESMIDI', N'Après-midi', N'SAGE', N'EMPLOYEE_EVENT', 0, N'apresmidi|après-midi|pm', 60),
                (N'PTE_USER_ID', N'Identifiant utilisateur', N'POINTEUSE', N'PUNCH_USER', 1, N'userid|user_id|id', 10),
                (N'PTE_USER_BADGE', N'Badge', N'POINTEUSE', N'PUNCH_USER', 0, N'badge|badgenumber|badge_number', 20),
                (N'PTE_USER_SSN', N'SSN / matricule', N'POINTEUSE', N'PUNCH_USER', 0, N'ssn|matricule|matric', 30),
                (N'PTE_USER_NAME', N'Nom', N'POINTEUSE', N'PUNCH_USER', 0, N'name|nom', 40),
                (N'PTE_PUNCH_USER_ID', N'Identifiant utilisateur', N'POINTEUSE', N'PUNCH', 1, N'userid|user_id|id', 10),
                (N'PTE_PUNCH_DATETIME', N'Date/heure', N'POINTEUSE', N'PUNCH', 1, N'checktime|check_time|datetime|date', 20),
                (N'PTE_PUNCH_TYPE', N'Type (entrée/sortie)', N'POINTEUSE', N'PUNCH', 0, N'checktype|check_type|type|sens', 30)
            ) AS v(Code, Label, SystemType, EntityKind, IsRequired, AutoMappingPatterns, SortOrder)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.T_FIELD_ROLE r WHERE r.Code = v.Code);
            """, ct);
    }

    // Convertit une fois les anciennes colonnes MAP_* en mappings (idempotent).
    private async Task MigrateLegacyMappingsAsync(CancellationToken ct)
    {
        var entRepo = _uow.Repository<T_SOURCE_ENTITY_MAPPING>();
        var fldRepo = _uow.Repository<T_SOURCE_FIELD_MAPPING>();
        var existing = await entRepo.ListAsync(_ => true, ct);
        var sageDone = existing.Where(e => e.SageDbId != null).Select(e => e.SageDbId!.Value).ToHashSet();
        var pteDone = existing.Where(e => e.PointeuseDbId != null).Select(e => e.PointeuseDbId!.Value).ToHashSet();

        foreach (var row in await _uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, ct))
        {
            if (sageDone.Contains(row.ID) || string.IsNullOrWhiteSpace(row.MAP_TABLE)) continue;
            var ent = new T_SOURCE_ENTITY_MAPPING { SystemType = "SAGE", SageDbId = row.ID, EntityKind = "EMPLOYEE", SourceTable = row.MAP_TABLE!.Trim() };
            await entRepo.AddAsync(ent, ct);
            await _uow.SaveChangesAsync(ct);
            await AddLegacyFieldsAsync(fldRepo, ent.Id, new[]
            {
                ("SAGE_MATRICULE", row.MAP_COL_MATRICULE),
                ("SAGE_NOM", row.MAP_COL_NOM),
                ("SAGE_PRENOM", row.MAP_COL_PRENOM),
                ("SAGE_BADGE", row.MAP_COL_BADGE),
            }, ct);
        }

        foreach (var row in await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, ct))
        {
            if (pteDone.Contains(row.ID)) continue;
            if (!string.IsNullOrWhiteSpace(row.MAP_USER_TABLE))
            {
                var ent = new T_SOURCE_ENTITY_MAPPING { SystemType = "POINTEUSE", PointeuseDbId = row.ID, EntityKind = "PUNCH_USER", SourceTable = row.MAP_USER_TABLE!.Trim() };
                await entRepo.AddAsync(ent, ct);
                await _uow.SaveChangesAsync(ct);
                await AddLegacyFieldsAsync(fldRepo, ent.Id, new[]
                {
                    ("PTE_USER_ID", row.MAP_USER_COL_ID),
                    ("PTE_USER_BADGE", row.MAP_USER_COL_BADGE),
                    ("PTE_USER_SSN", row.MAP_USER_COL_SSN),
                    ("PTE_USER_NAME", row.MAP_USER_COL_NOM),
                }, ct);
            }
            if (!string.IsNullOrWhiteSpace(row.MAP_PUNCH_TABLE))
            {
                var ent = new T_SOURCE_ENTITY_MAPPING { SystemType = "POINTEUSE", PointeuseDbId = row.ID, EntityKind = "PUNCH", SourceTable = row.MAP_PUNCH_TABLE!.Trim() };
                await entRepo.AddAsync(ent, ct);
                await _uow.SaveChangesAsync(ct);
                await AddLegacyFieldsAsync(fldRepo, ent.Id, new[]
                {
                    ("PTE_PUNCH_USER_ID", row.MAP_PUNCH_COL_USER_ID),
                    ("PTE_PUNCH_DATETIME", row.MAP_PUNCH_COL_DATETIME),
                    ("PTE_PUNCH_TYPE", row.MAP_PUNCH_COL_TYPE),
                }, ct);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    private static async Task AddLegacyFieldsAsync(IRepository<T_SOURCE_FIELD_MAPPING> repo, int entityMappingId, (string Role, string? Column)[] pairs, CancellationToken ct)
    {
        foreach (var (role, column) in pairs)
        {
            if (string.IsNullOrWhiteSpace(column)) continue;
            await repo.AddAsync(new T_SOURCE_FIELD_MAPPING
            {
                EntityMappingId = entityMappingId,
                FieldRoleCode = role,
                SourceColumn = column!.Trim()
            }, ct);
        }
    }

    public async Task<IReadOnlyList<FieldRoleDto>> GetFieldRolesAsync(string? systemType = null, CancellationToken ct = default)
    {
        await EnsureMappingSchemaAsync(ct);
        await MigrateLegacyMappingsAsync(ct);
        var list = await _uow.Repository<T_FIELD_ROLE>().ListAsync(_ => true, ct);
        return list
            .Where(r => string.IsNullOrWhiteSpace(systemType)
                        || string.Equals(r.SystemType, systemType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.SystemType)
            .ThenBy(r => r.EntityKind)
            .ThenBy(r => r.SortOrder)
            .Select(_mapper.Map<FieldRoleDto>)
            .ToList();
    }

    private static readonly Regex SafeSourceIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private static void ValidateSourceIdentifier(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !SafeSourceIdentifier.IsMatch(name))
            throw new InvalidOperationException($"Identifiant source invalide : {name}");
    }

    private async Task ValidateMappingsAsync(bool isSage, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct)
    {
        var system = isSage ? "SAGE" : "POINTEUSE";
        var roles = await _uow.Repository<T_FIELD_ROLE>().ListAsync(r => r.SystemType == system, ct);
        foreach (var m in mappings.Where(x => !string.IsNullOrWhiteSpace(x.EntityKind)))
        {
            if (string.IsNullOrWhiteSpace(m.SourceTable))
                throw new InvalidOperationException($"La table source est requise pour '{m.EntityKind}'.");
            ValidateSourceIdentifier(m.SourceTable);
            var fields = m.Fields ?? Array.Empty<SourceFieldMappingDto>();
            foreach (var f in fields)
                ValidateSourceIdentifier(f.SourceColumn);
            var provided = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.SourceColumn))
                .Select(f => f.FieldRoleCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = roles
                .Where(r => r.EntityKind == m.EntityKind && r.IsRequired && !provided.Contains(r.Code))
                .Select(r => r.Code)
                .ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Champs obligatoires non mappés pour '{m.EntityKind}' : {string.Join(", ", missing)}.");
        }
    }

    private async Task ReplaceMappingsAsync(bool isSage, int connectionId, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct)
    {
        var entRepo = _uow.Repository<T_SOURCE_ENTITY_MAPPING>();
        var fldRepo = _uow.Repository<T_SOURCE_FIELD_MAPPING>();
        var existing = await entRepo.ListAsync(m => isSage ? m.SageDbId == connectionId : m.PointeuseDbId == connectionId, ct);
        if (existing.Count > 0)
        {
            var ids = existing.Select(e => e.Id).ToHashSet();
            var existingFields = await fldRepo.ListAsync(f => ids.Contains(f.EntityMappingId), ct);
            fldRepo.RemoveRange(existingFields);
            entRepo.RemoveRange(existing);
            await _uow.SaveChangesAsync(ct);
        }
        foreach (var m in mappings.Where(x => !string.IsNullOrWhiteSpace(x.EntityKind)))
        {
            var ent = new T_SOURCE_ENTITY_MAPPING
            {
                SystemType = isSage ? "SAGE" : "POINTEUSE",
                SageDbId = isSage ? connectionId : null,
                PointeuseDbId = isSage ? null : connectionId,
                EntityKind = m.EntityKind.Trim(),
                SourceTable = string.IsNullOrWhiteSpace(m.SourceTable) ? null : m.SourceTable!.Trim()
            };
            await entRepo.AddAsync(ent, ct);
            await _uow.SaveChangesAsync(ct);
            foreach (var f in (m.Fields ?? Array.Empty<SourceFieldMappingDto>()).Where(x => !string.IsNullOrWhiteSpace(x.FieldRoleCode)))
            {
                await fldRepo.AddAsync(new T_SOURCE_FIELD_MAPPING
                {
                    EntityMappingId = ent.Id,
                    FieldRoleCode = f.FieldRoleCode.Trim(),
                    SourceColumn = string.IsNullOrWhiteSpace(f.SourceColumn) ? null : f.SourceColumn!.Trim()
                }, ct);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<SourceEntityMappingDto>> LoadMappingsForAsync(bool isSage, int connectionId, CancellationToken ct)
    {
        var lookup = await LoadMappingLookupAsync(isSage, ct);
        return lookup.TryGetValue(connectionId, out var list) ? list : Array.Empty<SourceEntityMappingDto>();
    }

    private async Task<Dictionary<int, List<SourceEntityMappingDto>>> LoadMappingLookupAsync(bool isSage, CancellationToken ct)
    {
        var entities = await _uow.Repository<T_SOURCE_ENTITY_MAPPING>().ListAsync(_ => true, ct);
        var fields = await _uow.Repository<T_SOURCE_FIELD_MAPPING>().ListAsync(_ => true, ct);
        var fieldsByEntity = fields.GroupBy(f => f.EntityMappingId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new Dictionary<int, List<SourceEntityMappingDto>>();
        foreach (var e in entities)
        {
            var key = isSage ? e.SageDbId : e.PointeuseDbId;
            if (key is not int id) continue;
            var dto = _mapper.Map<SourceEntityMappingDto>(e) with
            {
                Fields = (fieldsByEntity.TryGetValue(e.Id, out var fs) ? fs : new List<T_SOURCE_FIELD_MAPPING>())
                    .Select(_mapper.Map<SourceFieldMappingDto>)
                    .ToList()
            };
            if (!result.TryGetValue(id, out var list)) { list = new List<SourceEntityMappingDto>(); result[id] = list; }
            list.Add(dto);
        }
        return result;
    }

    private async Task<IReadOnlyList<string>> ValidateMappingColumnsAsync(string serveur, string nomBd, bool sqlAuth, string? login, string? password, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct)
    {
        var missing = new List<string>();
        foreach (var m in mappings.Where(x => !string.IsNullOrWhiteSpace(x.SourceTable)))
        {
            var cols = await _discovery.ListColumnsAsync(serveur, nomBd, m.SourceTable!, sqlAuth, login, password, ct);
            foreach (var col in (m.Fields ?? Array.Empty<SourceFieldMappingDto>())
                        .Where(f => !string.IsNullOrWhiteSpace(f.SourceColumn))
                        .Select(f => f.SourceColumn!))
            {
                if (!cols.Contains(col, StringComparer.OrdinalIgnoreCase))
                    missing.Add($"{m.SourceTable}.{col}");
            }
        }
        return missing;
    }

    // Le mode correspondance est rattache a la paire
    // selectionnee par l'utilisateur
    public async Task<CorrespondenceModeDto> GetCorrespondenceAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var row = (await _uow.Repository<T_ACTIVECORRESP>().ListAsync(a =>
            a.SAGE_SERVEUR == sageRow.SERVEUR && a.SAGE_BDD == sageRow.NOM_BD &&
            a.POINTEUSE_SERVEUR == pteRow.SERVEUR && a.POINTEUSE_BDD == pteRow.NOM_BD, ct)).FirstOrDefault();
        return new CorrespondenceModeDto(row?.Active == true);
    }
    public async Task<CorrespondenceModeDto> SetCorrespondenceAsync(bool active, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var repo = _uow.Repository<T_ACTIVECORRESP>();
        var row = (await repo.ListAsync(a =>
            a.SAGE_SERVEUR == sageRow.SERVEUR && a.SAGE_BDD == sageRow.NOM_BD &&
            a.POINTEUSE_SERVEUR == pteRow.SERVEUR && a.POINTEUSE_BDD == pteRow.NOM_BD, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new T_ACTIVECORRESP
            {
                Active = active,
                SAGE_SERVEUR = sageRow.SERVEUR,
                SAGE_BDD = sageRow.NOM_BD,
                POINTEUSE_SERVEUR = pteRow.SERVEUR,
                POINTEUSE_BDD = pteRow.NOM_BD,
                DATE_MODIF = DateTime.Now
            };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.Active = active;
            row.DATE_MODIF = DateTime.Now;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        if (!active)
        {
            // Desactivation du mode manuel : on relance immediatement l'auto-mapping
            var result = await SyncCardPaieCoreAsync(sageRow, pteRow, ct);
            row.DERNIERE_SYNC = DateTime.Now;
            repo.Update(row);
            await _uow.SaveChangesAsync(ct);
        }
        return new CorrespondenceModeDto(active);
    }

    public async Task<IReadOnlyList<CardPaieDto>> ListCardsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = (await _uow.Repository<T_CARDPAIE>().ListAsync(_ => true, ct))
            .Where(c =>
            {
                if (string.IsNullOrWhiteSpace(c.SAGE_BDD)) return true;
                return string.Equals(c.SAGE_BDD.Trim(), sage.Trim(), StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        return list.Select(_mapper.Map<CardPaieDto>).ToList();
    }

    public async Task<CardPaieDto> SaveCardAsync(CardPaieDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_CARDPAIE>();
        T_CARDPAIE entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<T_CARDPAIE>(dto);
            entity.SAGE_BDD ??= _tenant.SageDb;
            entity.DATE ??= DateTime.Now;
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            _mapper.Map(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CardPaieDto>(entity);
    }

    public async Task DeleteCardAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CARDPAIE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<SyncResultDto> AutoMapCardsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var mode = await GetCorrespondenceAsync(ct);
        if (mode.Active)
            throw new InvalidOperationException(
                "Le mode correspondance manuelle est actif pour cette paire de bases : la synchronisation automatique est désactivée. Désactivez le mode pour la relancer.");
        return await SyncCardPaieCoreAsync(sageRow, pteRow, ct);
    }
    
    public async Task<ActivationResultDto> ActivateSagePairAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);

        var holidaysImported = false;
        if (sageRow.TYPE_BASE == "STANDARD")
        {
            await ImportSageHolidaysAsync(ct);
            holidaysImported = true;
        }

        await EnsureCodeConstantesAsync(ct);
        var sync = await AutoMapCardsAsync(ct);

        return new ActivationResultDto(holidaysImported, sync.Added, sync.Deactivated, sync.Message);
    }
    private async Task<SyncResultDto> SyncCardPaieCoreAsync(T_BDD_SAGE sageRow, T_BDD_POINTEUSE pteRow, CancellationToken ct)
    {
        var employees = await _externalSource.GetEmployeesAsync(sageRow, ct);
        var badges = await _externalSource.GetPunchUsersAsync(pteRow, ct);
        var affectations = await _externalSource.GetAffectationsAsync(sageRow, ct);
        var departments = (await _externalSource.GetDepartmentsAsync(sageRow, ct))
            .GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        string? DepartementDe(long numSalarie)
        {
            var aff = affectations.Where(a => a.EmployeeId == numSalarie && a.DateSortie == null)
                                   .OrderByDescending(a => a.DateDebut).FirstOrDefault()
                      ?? affectations.Where(a => a.EmployeeId == numSalarie)
                                     .OrderByDescending(a => a.DateDebut).FirstOrDefault();
            if (aff?.Departement == null) return null;
            return departments.TryGetValue(aff.Departement, out var intitule) && !string.IsNullOrWhiteSpace(intitule)
                ? intitule : aff.Departement;
        }
        var repo = _uow.Repository<T_CARDPAIE>();
        // L'identite d'une carte est le salarie (matricule) dans une base SAGE : le reste de
        // l'application (ListCardsAsync, ImportFromClockAsync) raisonne par base SAGE et non par
        // paire de serveurs. Filtrer ici sur la paire recreait un jeu complet de cartes des que
        // le libelle du serveur ou la base pointeuse changeait.
        var sageDbName = (sageRow.NOM_BD ?? string.Empty).Trim();
        var scopeCards = (await repo.ListAsync(_ => true, ct))
            .Where(c => string.Equals((c.SAGE_BDD ?? string.Empty).Trim(), sageDbName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        static bool MemeIdentifiant(string? a, string? b)
        {
            a = a?.Trim();
            b = b?.Trim();
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (long.TryParse(a, out var na) && long.TryParse(b, out var nb))
                return na == nb;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        var matched = new List<string>();
        var seenEmployees = new HashSet<long>();
        var added = 0;
        foreach (var sal in employees)
        {
            var matricule = (sal.Matricule ?? string.Empty).Trim();
            // La source peut renvoyer plusieurs fois le meme salarie (matricule formate differemment) :
            // une seule carte doit etre produite par employe.
            if (matricule.Length == 0 || !seenEmployees.Add(sal.EmployeeId)) continue;
            var match = badges.FirstOrDefault(b =>
                MemeIdentifiant(b.Ssn, matricule)
                || MemeIdentifiant(b.Badge, matricule)
                || MemeIdentifiant(b.Badge, sal.Badge));
            if (match == null) continue;
            matched.Add(matricule);
            var departement = DepartementDe(sal.EmployeeId);
            // Comparaison par identifiant (00369 == 369) : un simple changement de format du
            // matricule ne doit pas creer une nouvelle carte.
            var existingCard = scopeCards.FirstOrDefault(c => MemeIdentifiant(c.SAGE_MATRICULE, matricule));
            if (existingCard == null)
            {
                var newCard = new T_CARDPAIE
                {
                    SAGE_MATRICULE = matricule,
                    SAGE_NOM = sal.Nom,
                    SAGE_PRENOM = sal.Prenom,
                    POINTEUSE_NUMERO = match.Badge,
                    POINTEUSE_NOM = match.Name,
                    SAGE_SERVEUR = sageRow.SERVEUR,
                    POINTEUSE_SERVEUR = pteRow.SERVEUR,
                    SAGE_BDD = sageRow.NOM_BD,
                    POINTEUSE_BDD = pteRow.NOM_BD,
                    DEPARTEMENT = departement,
                    ORIGINE = "AUTO",
                    ACTIF = true,
                    DATE = DateTime.Now
                };
                await repo.AddAsync(newCard, ct);
                scopeCards.Add(newCard); // sinon la carte creee n'est pas vue plus loin dans la meme passe
                added++;
            }
            else if (existingCard.ORIGINE == "AUTO")
            {
                existingCard.SAGE_NOM = sal.Nom;
                existingCard.SAGE_PRENOM = sal.Prenom;
                existingCard.POINTEUSE_NUMERO = match.Badge;
                existingCard.POINTEUSE_NOM = match.Name;
                // La carte suit la configuration courante : la paire est mise a jour au lieu
                // de creer une seconde carte pour le meme salarie.
                existingCard.SAGE_SERVEUR = sageRow.SERVEUR;
                existingCard.POINTEUSE_SERVEUR = pteRow.SERVEUR;
                existingCard.POINTEUSE_BDD = pteRow.NOM_BD;
                if (departement != null) existingCard.DEPARTEMENT = departement;
                existingCard.ACTIF = true;
                repo.Update(existingCard);
            }
        }
        var deactivated = 0;
        foreach (var card in scopeCards.Where(c => c.ORIGINE == "AUTO" && c.ACTIF))
        {
            if (matched.Any(m => MemeIdentifiant(m, card.SAGE_MATRICULE))) continue;
            var hasPunches = (await _uow.Repository<T_POINTAGE>().ListAsync(p =>
                p.NOM_BDD_SAGE == sageRow.NOM_BD && p.MATRICULE_SAGE == card.SAGE_MATRICULE, ct)).Any();
            var hasCategorie = (await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.MATRICULE_SAGE == card.ID, ct)).Any();
            if (hasPunches || hasCategorie) continue; // donnees liees : jamais desactivee ni supprimee
            card.ACTIF = false;
            repo.Update(card);
            deactivated++;
        }
        await _uow.SaveChangesAsync(ct);
        return new SyncResultDto(added, deactivated,
            $"{added} carte(s) ajoutée(s), {deactivated} mise(s) de côté (plus de correspondance trouvée).");
    }

    public async Task<ImportResultDto> ImportCorrespondencesCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_CARDPAIE>();
        var existing = await repo.ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var parts in lines)
        {
            if (parts.Length < 2)
            {
                skipped++;
                continue;
            }
            var badge = parts[0].Trim();
            var matricule = parts[1].Trim();
            var badgeFold = ExcelCells.Fold(badge);
            if (badgeFold is "BADGE" or "POINTEUSE" or "NPOINTEUSE" or "NUMERO")
            {
                skipped++;
                continue;
            }
            if (string.IsNullOrWhiteSpace(matricule)
                || ExcelCells.Fold(matricule) is "MATRICULE" or "MATRICULESAGE")
            {
                skipped++;
                continue;
            }
            var card = existing.FirstOrDefault(c => string.Equals(c.SAGE_MATRICULE, matricule, StringComparison.OrdinalIgnoreCase));
            if (card == null)
            {
                card = new T_CARDPAIE
                {
                    SAGE_MATRICULE = matricule,
                    POINTEUSE_NUMERO = badge,
                    SAGE_BDD = _tenant.SageDb,
                    POINTEUSE_BDD = _tenant.PointeuseDb,
                    DATE = DateTime.Now
                };
                await repo.AddAsync(card, ct);
                existing.Add(card);
            }
            else
            {
                card.POINTEUSE_NUMERO = badge;
                repo.Update(card);
            }
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Correspondances importées.");
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == sage, ct);
        return list.Select(_mapper.Map<CategoryDto>).ToList();
    }

    public async Task<CategoryDto> SaveCategoryAsync(CategoryDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);

        var cardRepo = _uow.Repository<T_CARDPAIE>();
        var supervisorCard = await cardRepo.GetByIdAsync(dto.SupervisorCardId, ct);
        if (supervisorCard == null)
            throw new KeyNotFoundException($"Carte superviseur introuvable (ID={dto.SupervisorCardId}).");

        if (string.IsNullOrWhiteSpace(dto.Intitule))
            throw new InvalidOperationException("L'intitulé de la catégorie est obligatoire.");

        if (dto.HeuresSemaine <= 0)
            throw new InvalidOperationException("Le nombre d'heures par semaine doit être supérieur à 0.");

        var repo = _uow.Repository<T_CATEGORIE>();
        T_CATEGORIE entity;
        if (dto.Id == 0)
        {
            entity = new T_CATEGORIE
            {
                INTITULE = dto.Intitule,
                ID_CARDPAIE = dto.SupervisorCardId,
                PAUSE = dto.Pause,
                HEURESEMAINE = dto.HeuresSemaine,
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb
            };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.INTITULE = dto.Intitule;
            entity.ID_CARDPAIE = dto.SupervisorCardId;
            entity.PAUSE = dto.Pause;
            entity.HEURESEMAINE = dto.HeuresSemaine;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CategoryDto>(entity);
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CATEGORIE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AffectationDto>> ListAffectationsAsync(int? categoryId, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<T_CAT_SAL>().ListAsync(
            a => a.BDD_SAGE == sage && (!categoryId.HasValue || a.ID_CATEG == categoryId), ct);
        return list.Select(a => new AffectationDto(a.ID, a.MATRICULE_SAGE, a.ID_CATEG)).ToList();
    }

    public async Task<AffectationDto> SaveAffectationAsync(AffectationDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);

        var cardExists = await _uow.Repository<T_CARDPAIE>().GetByIdAsync(dto.CardPaieId, ct);
        if (cardExists == null)
            throw new KeyNotFoundException($"Carte salarié introuvable (ID={dto.CardPaieId}).");

        var categoryExists = await _uow.Repository<T_CATEGORIE>().GetByIdAsync(dto.CategoryId, ct);
        if (categoryExists == null)
            throw new KeyNotFoundException($"Catégorie introuvable (ID={dto.CategoryId}).");

        var repo = _uow.Repository<T_CAT_SAL>();
        T_CAT_SAL entity;

        if (dto.Id == 0)
        {
            var existingForCard = (await repo.ListAsync(a => a.MATRICULE_SAGE == dto.CardPaieId, ct)).FirstOrDefault();
            if (existingForCard != null)
            {
                existingForCard.ID_CATEG = dto.CategoryId;
                repo.Update(existingForCard);
                entity = existingForCard;
            }
            else
            {
                entity = new T_CAT_SAL
                {
                    MATRICULE_SAGE = dto.CardPaieId,
                    ID_CATEG = dto.CategoryId,
                    BDD_SAGE = _tenant.SageDb,
                    BDD_POINTEUSE = _tenant.PointeuseDb
                };
                await repo.AddAsync(entity, ct);
            }
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.MATRICULE_SAGE = dto.CardPaieId;
            entity.ID_CATEG = dto.CategoryId;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new AffectationDto(entity.ID, entity.MATRICULE_SAGE, entity.ID_CATEG);
    }

    public async Task<ImportResultDto> ImportAffectationsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var cats = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        var repo = _uow.Repository<T_CAT_SAL>();
        var existing = await repo.ListAsync(a => a.BDD_SAGE == _tenant.SageDb, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var catName = ExcelCells.Get(row, "Categorie", "CATEGORIE", "Intitule", "Intitulé")
                        ?? row.Values.FirstOrDefault();
            var mat = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE")
                    ?? (row.Count > 1 ? row.Values.Skip(1).FirstOrDefault() : null);
            if (string.IsNullOrWhiteSpace(catName) || string.IsNullOrWhiteSpace(mat))
            {
                skipped++;
                continue;
            }
            var cat = cats.FirstOrDefault(c => string.Equals(c.INTITULE?.Trim(), catName.Trim(), StringComparison.OrdinalIgnoreCase));
            var card = cards.FirstOrDefault(c => string.Equals(c.SAGE_MATRICULE?.Trim(), mat.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cat == null || card == null)
            {
                skipped++;
                continue;
            }
            var found = existing.FirstOrDefault(a => a.MATRICULE_SAGE == card.ID);
            if (found == null)
            {
                found = new T_CAT_SAL
                {
                    MATRICULE_SAGE = card.ID,
                    ID_CATEG = cat.IDCATEGORIE,
                    BDD_SAGE = _tenant.SageDb,
                    BDD_POINTEUSE = _tenant.PointeuseDb
                };
                await repo.AddAsync(found, ct);
                existing.Add(found);
            }
            else
            {
                found.ID_CATEG = cat.IDCATEGORIE;
                repo.Update(found);
            }
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Affectations importées.");
    }

    public async Task DeleteAffectationAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CAT_SAL>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ToleranceDto>> ListTolerancesAsync(int? categoryId, CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_TOLERANCE>().ListAsync(
            t => !categoryId.HasValue || t.IDCATEGORIE == categoryId, ct);
        return list.Select(t => new ToleranceDto(t.ID, t.IDCATEGORIE, t.TOLERANCE, t.TYPES_TOL, t.SORTIE)).ToList();
    }

    public async Task<ToleranceDto> SaveToleranceAsync(ToleranceDto dto, CancellationToken ct = default)
    {
        var categoryExists = await _uow.Repository<T_CATEGORIE>().GetByIdAsync(dto.CategoryId, ct);
        if (categoryExists == null)
            throw new KeyNotFoundException($"Catégorie introuvable (ID={dto.CategoryId}).");

        if (dto.Tolerance < 0)
            throw new InvalidOperationException("La tolérance d'entrée ne peut pas être négative.");

        if (dto.Sortie < 0)
            throw new InvalidOperationException("La tolérance de sortie ne peut pas être négative.");

        var repo = _uow.Repository<T_TOLERANCE>();
        T_TOLERANCE entity;
        if (dto.Id == 0)
        {
            entity = new T_TOLERANCE
            {
                IDCATEGORIE = dto.CategoryId,
                TOLERANCE = dto.Tolerance,
                TYPES_TOL = dto.TypesTol,
                SORTIE = dto.Sortie
            };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.IDCATEGORIE = dto.CategoryId;
            entity.TOLERANCE = dto.Tolerance;
            entity.TYPES_TOL = dto.TypesTol;
            entity.SORTIE = dto.Sortie;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new ToleranceDto(entity.ID, entity.IDCATEGORIE, entity.TOLERANCE, entity.TYPES_TOL, entity.SORTIE);
    }

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sage, ct);
        return list.Select(_mapper.Map<HolidayDto>).ToList();
    }

    public async Task<HolidayDto> SaveHolidayAsync(HolidayDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_FERIE>();
        T_FERIE entity;
        if (dto.Id == 0)
        {
            var duplicate = dto.Date.HasValue
                ? (await repo.ListAsync(f => f.DATE.HasValue && f.DATE.Value.Date == dto.Date.Value.Date, ct)).FirstOrDefault()
                : null;
            if (duplicate != null)
            {
                duplicate.INTITULE = dto.Intitule;
                repo.Update(duplicate);
                entity = duplicate;
            }
            else
            {
                entity = new T_FERIE { INTITULE = dto.Intitule, DATE = dto.Date };
                await repo.AddAsync(entity, ct);
            }
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.INTITULE = dto.Intitule;
            entity.DATE = dto.Date;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<HolidayDto>(entity);
    }

    public async Task DeleteHolidayAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_FERIE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ImportSageHolidaysAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var holidays = await _externalSource.GetCompanyCalendarAsync(sageRow, ct);
        var repo = _uow.Repository<T_FERIE>();
        var existing = await repo.ListAsync(f => f.BDD_SAGE == _tenant.SageDb, ct);
        foreach (var day in holidays)
        {
            var date = day.Date;
            if (existing.Any(e => e.DATE.HasValue && e.DATE.Value.Date == date))
                continue;
            await repo.AddAsync(new T_FERIE { DATE = date, INTITULE = "Férié SAGE", BDD_SAGE = _tenant.SageDb }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MajorationDto>> ListMajorationsAsync(CancellationToken ct = default)
    {
        // Desktop (RExtensions.getListMajoration) returns every row: the SAGE/pointeuse
        // filter is commented out, and existing data often has BDD_SAGE null.
        var list = await _uow.Repository<T_MAJORATION>().ListAsync(_ => true, ct);
        return list.OrderBy(m => m.Cotation).Select(_mapper.Map<MajorationDto>).ToList();
    }

    public async Task<MajorationDto> SaveMajorationAsync(MajorationDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_MAJORATION>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
        entity.Cotation = dto.Cotation;
        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<MajorationDto>(entity);
    }

    public async Task<IReadOnlyList<AbsenceCodeDto>> ListAbsenceCodesAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_CODEABSENCE>().ListAsync(_ => true, ct);
        return list.Select(a => new AbsenceCodeDto(a.ID_ABSENCE, a.INTITULE_ABSENCE, a.NOT_PAY)).ToList();
    }

    public async Task<AbsenceCodeDto> SaveAbsenceCodeAsync(AbsenceCodeDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CODEABSENCE>();
        T_CODEABSENCE entity;
        if (dto.Id == 0)
        {
            entity = new T_CODEABSENCE { INTITULE_ABSENCE = dto.Intitule, NOT_PAY = dto.NotPay };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.INTITULE_ABSENCE = dto.Intitule;
            entity.NOT_PAY = dto.NotPay;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new AbsenceCodeDto(entity.ID_ABSENCE, entity.INTITULE_ABSENCE, entity.NOT_PAY);
    }

    public async Task<IReadOnlyList<CodeConstanteDto>> ListCodeConstantesAsync(CancellationToken ct = default)
    {
        await EnsureCodeConstantesAsync(ct);
        var list = await _uow.Repository<T_CODE_CONSTANTE>().ListAsync(_ => true, ct);
        var order = OvertimeCodeKeys.Defaults.Select((d, i) => (d.Key, i)).ToDictionary(x => x.Key, x => x.i);
        return list
            .OrderBy(c => order.TryGetValue(c.CATEGORIE, out var i) ? i : 99)
            .Select(_mapper.Map<CodeConstanteDto>)
            .ToList();
    }

    public async Task<CodeConstanteDto> SaveCodeConstanteAsync(CodeConstanteDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_CODE_CONSTANTE>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
        entity.CODE_CONSTANTE = (dto.CodeConstante ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(entity.CODE_CONSTANTE))
            throw new InvalidOperationException("Le code constante SAGE est requis.");
        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CodeConstanteDto>(entity);
    }

    public async Task<IReadOnlyList<SageConstantOptionDto>> ListSageConstantOptionsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var constants = await _externalSource.GetConstantsAsync(sageRow, ct);
        return constants
            .GroupBy(c => (c.Code ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .OrderBy(g => g.Key)
            .Select(g => new SageConstantOptionDto(g.Key, g.First().Label))
            .ToList();
    }

    public async Task<OvertimeSageCodes> GetOvertimeSageCodesAsync(CancellationToken ct = default)
    {
        await EnsureCodeConstantesAsync(ct);
        var list = await _uow.Repository<T_CODE_CONSTANTE>().ListAsync(_ => true, ct);
        string Code(string key, string fallback) =>
            list.FirstOrDefault(c => c.CATEGORIE == key)?.CODE_CONSTANTE is { Length: > 0 } v ? v.Trim() : fallback;

        return new OvertimeSageCodes(
            Code(OvertimeCodeKeys.Exo130, "HS01"),
            Code(OvertimeCodeKeys.Exo150, "HS02"),
            Code(OvertimeCodeKeys.I130, "HS03"),
            Code(OvertimeCodeKeys.I150, "HS04"),
            Code(OvertimeCodeKeys.Ferie, "HS05"),
            Code(OvertimeCodeKeys.Dim, "HS06"),
            Code(OvertimeCodeKeys.Nuit, "HS07"));
    }

    private async Task EnsureCodeConstantesAsync(CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await _uow.ExecuteSqlAsync("""
            IF OBJECT_ID(N'dbo.T_CODE_CONSTANTE', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_CODE_CONSTANTE (
                    ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    CATEGORIE nvarchar(32) NOT NULL,
                    INTITULE nvarchar(64) NULL,
                    CODE_CONSTANTE nvarchar(20) NOT NULL
                );
            END
            ELSE IF COL_LENGTH(N'dbo.T_CODE_CONSTANTE', N'BDD_SAGE') IS NOT NULL
            BEGIN
                DELETE FROM dbo.T_CODE_CONSTANTE
                WHERE ID NOT IN (SELECT MIN(ID) FROM dbo.T_CODE_CONSTANTE GROUP BY CATEGORIE);
                ALTER TABLE dbo.T_CODE_CONSTANTE DROP COLUMN BDD_SAGE;
            END
            """, ct);

        var repo = _uow.Repository<T_CODE_CONSTANTE>();
        var existing = await repo.ListAsync(_ => true, ct);
        var added = false;
        foreach (var (key, intitule, defaultCode) in OvertimeCodeKeys.Defaults)
        {
            if (existing.Any(c => c.CATEGORIE == key))
                continue;
            await repo.AddAsync(new T_CODE_CONSTANTE
            {
                CATEGORIE = key,
                INTITULE = intitule,
                CODE_CONSTANTE = defaultCode,
            }, ct);
            added = true;
        }
        if (added)
            await _uow.SaveChangesAsync(ct);
    }

    public async Task SyncAbsenceCodesFromSageAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var events = await _externalSource.GetEventsAsync(sageRow, ct);
        var repo = _uow.Repository<T_CODEABSENCE>();
        var existing = await repo.ListAsync(_ => true, ct);
        foreach (var ev in events)
        {
            if (existing.Any(e => e.INTITULE_ABSENCE == ev.Label || e.INTITULE_ABSENCE == ev.Code))
                continue;
            var newCode = new T_CODEABSENCE { INTITULE_ABSENCE = ev.Label ?? ev.Code, NOT_PAY = false };
            await repo.AddAsync(newCode, ct);
            existing.Add(newCode);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<TDto>> MapList<TEntity, TDto>(CancellationToken ct) where TEntity : class
    {
        var list = await _uow.Repository<TEntity>().ListAsync(_ => true, ct);
        return list.Select(e => _mapper.Map<TDto>(e)).ToList();
    }
}