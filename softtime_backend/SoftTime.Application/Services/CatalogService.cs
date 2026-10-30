using System.Text.RegularExpressions;
using AutoMapper;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
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

    public async Task<SageDbDto?> GetSageByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<T_BDD_SAGE>().GetByIdAsync(id, ct);
        return entity == null ? null : _mapper.Map<SageDbDto>(entity);
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
        await RemoveMappingsAsync(true, id, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    // Supprime les mappings d'entite (et leurs champs) d'une connexion. Sans cela les lignes
    // restent orphelines et bloquent la reutilisation de l'index unique (SageDbId/PointeuseDbId, EntityKind).
    private async Task RemoveMappingsAsync(bool isSage, int connectionId, CancellationToken ct)
    {
        var entRepo = _uow.Repository<T_SOURCE_ENTITY_MAPPING>();
        var fldRepo = _uow.Repository<T_SOURCE_FIELD_MAPPING>();
        var existing = await entRepo.ListAsync(m => isSage ? m.SageDbId == connectionId : m.PointeuseDbId == connectionId, ct);
        if (existing.Count == 0) return;
        var ids = existing.Select(x => x.Id).ToHashSet();
        var fields = await fldRepo.ListAsync(f => ids.Contains(f.EntityMappingId), ct);
        fldRepo.RemoveRange(fields);
        entRepo.RemoveRange(existing);
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

    public async Task<PointeuseDbDto?> GetPointeuseByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<T_BDD_POINTEUSE>().GetByIdAsync(id, ct);
        return entity == null ? null : _mapper.Map<PointeuseDbDto>(entity);
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
        await RemoveMappingsAsync(false, id, ct);
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

    /// <summary>
    /// Options de departement / service proposees pour une base AUTRE.
    /// Adapte au nouveau schema : elles proviennent du mapping de l'entite AFFECTATION
    /// (roles SAGE_AFF_DEPARTEMENT / SAGE_AFF_SERVICE) et non plus des colonnes MAP_*.
    /// </summary>
    public async Task<DepartementServiceOptionsDto> GetDepartementServiceOptionsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var database = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(database.TYPE_BASE, "AUTRE", StringComparison.OrdinalIgnoreCase))
            return new DepartementServiceOptionsDto(Array.Empty<string>(), Array.Empty<string>());

        var entity = (await _uow.Repository<T_SOURCE_ENTITY_MAPPING>()
                .ListAsync(m => m.SageDbId == database.ID && m.EntityKind == "AFFECTATION", ct))
            .FirstOrDefault();
        if (entity is null || string.IsNullOrWhiteSpace(entity.SourceTable))
            return new DepartementServiceOptionsDto(Array.Empty<string>(), Array.Empty<string>());

        var fields = await _uow.Repository<T_SOURCE_FIELD_MAPPING>()
            .ListAsync(f => f.EntityMappingId == entity.Id, ct);
        string? ColumnOf(string role) => fields
            .FirstOrDefault(f => string.Equals(f.FieldRoleCode, role, StringComparison.OrdinalIgnoreCase))
            ?.SourceColumn?.Trim();

        async Task<IReadOnlyList<string>> ValuesAsync(string role)
        {
            var column = ColumnOf(role);
            if (string.IsNullOrWhiteSpace(column))
                return Array.Empty<string>();
            ValidateSourceIdentifier(entity.SourceTable);
            ValidateSourceIdentifier(column);
            return await _discovery.ListDistinctValuesAsync(
                database.SERVEUR!, database.NOM_BD!, entity.SourceTable!, column!,
                database.TYPE_AUTH == true, database.TLOGIN, database.TMDP, ct);
        }

        return new DepartementServiceOptionsDto(
            await ValuesAsync("SAGE_AFF_DEPARTEMENT"),
            await ValuesAsync("SAGE_AFF_SERVICE"));
    }

    // --- Configuration de l'ecriture de paie dynamique (bases AUTRE) ---

    public async Task<IReadOnlyList<PayrollWriteConfigurationDto>> ListPayrollWriteConfigurationsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var list = await _uow.Repository<T_PAIE_ECRITURE>().ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
        return list.OrderBy(c => Array.IndexOf(PayrollWritingConfiguration.Categories, c.CATEGORIE))
            .Select(c => new PayrollWriteConfigurationDto(c.ID, c.TABLE_CIBLE, c.COL_MATRICULE, c.CATEGORIE, c.COL_VALEUR)).ToList();
    }

    public async Task<PayrollWriteConfigurationDto> SavePayrollWriteConfigurationAsync(PayrollWriteConfigurationDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var database = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(database.TYPE_BASE, "AUTRE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La configuration d'écriture dynamique est réservée aux bases AUTRE.");
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var category = PayrollWritingConfiguration.NormalizeCategory(dto.Categorie);
        PayrollWritingConfiguration.ValidateIdentifier(dto.TableCible, "La table cible", allowQualifiedTable: true);
        PayrollWritingConfiguration.ValidateIdentifier(dto.ColMatricule, "La colonne matricule");
        PayrollWritingConfiguration.ValidateIdentifier(dto.ColValeur, "La colonne de valeur");
        var repo = _uow.Repository<T_PAIE_ECRITURE>();
        var current = await repo.ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
        if (current.Any(c => c.ID != dto.Id && string.Equals(c.CATEGORIE, category, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"La catégorie {category} est déjà configurée pour cette base.");
        T_PAIE_ECRITURE entity;
        if (dto.Id == 0)
        {
            entity = new T_PAIE_ECRITURE { BDD_SAGE = _tenant.SageDb };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            if (!string.Equals(entity.BDD_SAGE, _tenant.SageDb, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cette configuration appartient à une autre base.");
            repo.Update(entity);
        }
        entity.TABLE_CIBLE = dto.TableCible!.Trim();
        entity.COL_MATRICULE = dto.ColMatricule!.Trim();
        entity.CATEGORIE = category;
        entity.COL_VALEUR = dto.ColValeur!.Trim();
        await _uow.SaveChangesAsync(ct);
        return new PayrollWriteConfigurationDto(entity.ID, entity.TABLE_CIBLE, entity.COL_MATRICULE, entity.CATEGORIE, entity.COL_VALEUR);
    }

    public async Task DeletePayrollWriteConfigurationAsync(int id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var entity = await _uow.Repository<T_PAIE_ECRITURE>().GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        if (!string.Equals(entity.BDD_SAGE, _tenant.SageDb, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cette configuration appartient à une autre base.");
        _uow.Repository<T_PAIE_ECRITURE>().Remove(entity);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ConnectionTestResultDto> TestPayrollWriteConfigurationAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var database = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(database.TYPE_BASE, "AUTRE", StringComparison.OrdinalIgnoreCase))
            return new ConnectionTestResultDto(false, "Cette configuration est réservée aux bases AUTRE.");
        try
        {
            await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
            var configs = await _uow.Repository<T_PAIE_ECRITURE>().ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
            var missing = PayrollWritingConfiguration.Categories.FirstOrDefault(category => !configs.Any(c => string.Equals(c.CATEGORIE, category, StringComparison.OrdinalIgnoreCase)));
            if (missing != null)
                return new ConnectionTestResultDto(false, $"La configuration d'écriture de la base AUTRE est incomplète : la catégorie {missing} n'est pas configurée.");
            var first = configs.First();
            if (configs.Any(c => !string.Equals(c.TABLE_CIBLE, first.TABLE_CIBLE, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(c.COL_MATRICULE, first.COL_MATRICULE, StringComparison.OrdinalIgnoreCase)))
                return new ConnectionTestResultDto(false, "Toutes les catégories doivent utiliser la même table cible et la même colonne matricule.");
            var (schema, table) = PayrollWritingConfiguration.SplitTable(first.TABLE_CIBLE);
            if (!string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase))
                return new ConnectionTestResultDto(false, "La découverte des tables ne prend actuellement en charge que le schéma dbo.");
            var tables = await _discovery.ListTablesAsync(database.SERVEUR!, database.NOM_BD!, database.TYPE_AUTH == true, database.TLOGIN, database.TMDP, ct);
            if (!tables.Contains(table, StringComparer.OrdinalIgnoreCase))
                return new ConnectionTestResultDto(false, $"La table {first.TABLE_CIBLE} n'existe pas.");
            var columns = await _discovery.ListColumnsAsync(database.SERVEUR!, database.NOM_BD!, table, database.TYPE_AUTH == true, database.TLOGIN, database.TMDP, ct);
            var required = configs.Select(c => c.COL_VALEUR).Append(first.COL_MATRICULE).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var missingColumn = required.FirstOrDefault(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase));
            return missingColumn == null
                ? new ConnectionTestResultDto(true, "Configuration d'écriture valide.")
                : new ConnectionTestResultDto(false, $"La colonne {missingColumn} n'existe pas.");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResultDto(false, $"Validation impossible : {ex.Message}");
        }
    }

    /// <summary>
    /// Tables candidates pour le choix d'un code constante sur une base AUTRE.
    /// Adapte au nouveau schema : la table provient du mapping CONSTANT (role SAGE_CST_CODE).
    /// </summary>
    public async Task<IReadOnlyList<string>> ListOtherCodeConstantTablesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var (table, _) = await GetOtherCodeConstantMappingAsync(ct);
        return new[] { table };
    }

    /// <summary>
    /// Valeurs distinctes de la colonne de code constante pour une base AUTRE.
    /// Adapte au nouveau schema : table et colonne proviennent du mapping CONSTANT
    /// (le parametre <paramref name="table"/> n'est conserve que pour la compatibilite de l'API).
    /// </summary>
    public async Task<IReadOnlyList<string>> ListOtherCodeConstantValuesAsync(string table, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await ListMappedCodeConstantValuesAsync(ct);
    }

    // Valeurs distinctes de la colonne de code constante definie par le mapping CONSTANT.
    private async Task<IReadOnlyList<string>> ListMappedCodeConstantValuesAsync(CancellationToken ct)
    {
        var database = await _tenant.GetSageRowAsync(ct);
        var (mappedTable, column) = await GetOtherCodeConstantMappingAsync(ct);
        ValidateSourceIdentifier(mappedTable);
        ValidateSourceIdentifier(column);
        return await _discovery.ListDistinctValuesAsync(database.SERVEUR!, database.NOM_BD!, mappedTable, column,
            database.TYPE_AUTH == true, database.TLOGIN, database.TMDP, ct);
    }

    // Resout la table/colonne du code constante depuis le mapping de l'entite CONSTANT.
    private async Task<(string Table, string Column)> GetOtherCodeConstantMappingAsync(CancellationToken ct)
    {
        var database = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(database.TYPE_BASE, "AUTRE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cette liste est réservée aux bases AUTRE.");
        var entity = (await _uow.Repository<T_SOURCE_ENTITY_MAPPING>()
                .ListAsync(m => m.SageDbId == database.ID && m.EntityKind == "CONSTANT", ct))
            .FirstOrDefault();
        if (entity is null || string.IsNullOrWhiteSpace(entity.SourceTable))
            throw new InvalidOperationException("Configurez d'abord le mapping « Code constante » (table et colonne) dans Bases RH / paie.");
        var field = (await _uow.Repository<T_SOURCE_FIELD_MAPPING>()
                .ListAsync(f => f.EntityMappingId == entity.Id && f.FieldRoleCode == "SAGE_CST_CODE", ct))
            .FirstOrDefault();
        if (field is null || string.IsNullOrWhiteSpace(field.SourceColumn))
            throw new InvalidOperationException("La colonne de code constante n'est pas mappée (rôle SAGE_CST_CODE).");
        return (entity.SourceTable!.Trim(), field.SourceColumn!.Trim());
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

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND has_filter = 0)
                DROP INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (SageDbId, EntityKind) WHERE SageDbId IS NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND has_filter = 0)
            -- Index uniques FILTRES : pour une ligne SAGE, PointeuseDbId est NULL (et inversement).
            -- Sans filtre, SQL Server considere tous les NULL comme egaux et n'autoriserait qu'UNE
            -- seule ligne par EntityKind pour tout le systeme (2e base Sage/pointeuse refusee).
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND filter_definition IS NULL)
                DROP INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (SageDbId, EntityKind) WHERE SageDbId IS NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND filter_definition IS NULL)
                DROP INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (PointeuseDbId, EntityKind) WHERE PointeuseDbId IS NOT NULL;
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
                (N'SAGE_CIVILITE', N'Civilité', N'SAGE', N'EMPLOYEE', 0, N'civilite|civilité|sexe|genre', 70),
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

    /// <summary>
    /// Variante ciblée : ne charge que les mappings des connexions de la page courante, pour éviter
    /// de relire toute la table à chaque page. Les champs sont limités aux mappings sélectionnés.
    /// </summary>
    private async Task<Dictionary<int, List<SourceEntityMappingDto>>> LoadMappingLookupAsync(
        bool isSage, IReadOnlyCollection<int> connectionIds, CancellationToken ct)
    {
        if (connectionIds.Count == 0)
            return new Dictionary<int, List<SourceEntityMappingDto>>();

        var ids = connectionIds.Distinct().ToList();
        var entities = isSage
            ? await _uow.Repository<T_SOURCE_ENTITY_MAPPING>().ListAsync(
                e => e.SageDbId.HasValue && ids.Contains(e.SageDbId.Value), ct)
            : await _uow.Repository<T_SOURCE_ENTITY_MAPPING>().ListAsync(
                e => e.PointeuseDbId.HasValue && ids.Contains(e.PointeuseDbId.Value), ct);
        if (entities.Count == 0)
            return new Dictionary<int, List<SourceEntityMappingDto>>();

        var entityIds = entities.Select(e => e.Id).ToList();
        var fields = await _uow.Repository<T_SOURCE_FIELD_MAPPING>().ListAsync(
            f => entityIds.Contains(f.EntityMappingId), ct);
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

    // ---------------------------------------------------------------- Listes paginées (Lot A)

    public async Task<PagedResult<SageDbDto>> ListSagePagedAsync(
        IReadOnlyCollection<string>? allowedNames, int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await EnsureMappingSchemaAsync(ct);
        await MigrateLegacyMappingsAsync(ct);

        if (allowedNames is { Count: 0 })
        {
            var (_, taille) = PaginationRules.Normaliser(page, pageSize);
            return new PagedResult<SageDbDto>(Array.Empty<SageDbDto>(), 0, 1, taille, 1);
        }

        System.Linq.Expressions.Expression<Func<T_BDD_SAGE, bool>> predicate = allowedNames is null
            ? s => true
            : s => s.NOM_BD != null && allowedNames.Contains(s.NOM_BD);
        var searchPredicate = SearchExpression.For<T_BDD_SAGE>(
            search, s => s.SERVEUR, s => s.NOM_BD, s => s.TYPE_BASE);
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        var result = await _uow.Repository<T_BDD_SAGE>().ToPagedAsync(
            predicate, q => q.OrderBy(s => s.ID), page, pageSize, ct);
        if (result.Total > 0 && result.Items.Count == 0 && result.Page > result.TotalPages)
            result = await _uow.Repository<T_BDD_SAGE>().ToPagedAsync(
                predicate, q => q.OrderBy(s => s.ID), result.TotalPages, result.PageSize, ct);
        var mappings = await LoadMappingLookupAsync(true, result.Items.Select(s => s.ID).ToList(), ct);
        var items = result.Items.Select(s => _mapper.Map<SageDbDto>(s) with
        {
            Mappings = mappings.TryGetValue(s.ID, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
        var pageEffective = PaginationRules.PageEffective(result.Total, result.Page);
        return new PagedResult<SageDbDto>(items, result.Total, pageEffective, result.PageSize, result.TotalPages);
    }

    public async Task<PagedResult<PointeuseDbDto>> ListPointeusePagedAsync(
        IReadOnlyCollection<string>? allowedNames, int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await EnsureMappingSchemaAsync(ct);
        await MigrateLegacyMappingsAsync(ct);

        if (allowedNames is { Count: 0 })
        {
            var (_, taille) = PaginationRules.Normaliser(page, pageSize);
            return new PagedResult<PointeuseDbDto>(Array.Empty<PointeuseDbDto>(), 0, 1, taille, 1);
        }

        System.Linq.Expressions.Expression<Func<T_BDD_POINTEUSE, bool>> predicate = allowedNames is null
            ? p => true
            : p => p.NOM_BD != null && allowedNames.Contains(p.NOM_BD);
        var searchPredicate = SearchExpression.For<T_BDD_POINTEUSE>(
            search, p => p.SERVEUR, p => p.NOM_BD, p => p.TYPE_BASE);
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        var result = await _uow.Repository<T_BDD_POINTEUSE>().ToPagedAsync(
            predicate, q => q.OrderBy(p => p.ID), page, pageSize, ct);
        if (result.Total > 0 && result.Items.Count == 0 && result.Page > result.TotalPages)
            result = await _uow.Repository<T_BDD_POINTEUSE>().ToPagedAsync(
                predicate, q => q.OrderBy(p => p.ID), result.TotalPages, result.PageSize, ct);
        var mappings = await LoadMappingLookupAsync(false, result.Items.Select(p => p.ID).ToList(), ct);
        var items = result.Items.Select(p => _mapper.Map<PointeuseDbDto>(p) with
        {
            Mappings = mappings.TryGetValue(p.ID, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
        var pageEffective = PaginationRules.PageEffective(result.Total, result.Page);
        return new PagedResult<PointeuseDbDto>(items, result.Total, pageEffective, result.PageSize, result.TotalPages);
    }

    public async Task<PagedResult<CardPaieDto>> ListCardsPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = (_tenant.SageDb ?? string.Empty).Trim().ToLowerInvariant();
        System.Linq.Expressions.Expression<Func<T_CARDPAIE, bool>> predicate = c =>
            c.SAGE_BDD == null
            || c.SAGE_BDD.Trim() == ""
            || c.SAGE_BDD.Trim().ToLower() == sage;

        return await PagedLists.PageAsync(
            _uow.Repository<T_CARDPAIE>(),
            predicate,
            q => q.OrderBy(c => c.ID),
            page,
            pageSize,
            c => _mapper.Map<CardPaieDto>(c),
            search,
            new System.Linq.Expressions.Expression<Func<T_CARDPAIE, string?>>[]
            {
                c => c.SAGE_MATRICULE,
                c => c.SAGE_NOM,
                c => c.SAGE_PRENOM,
                c => c.POINTEUSE_NUMERO,
                c => c.POINTEUSE_NOM,
                c => c.DEPARTEMENT,
            },
            ct);
    }

    public async Task<PagedResult<CategoryDto>> ListCategoriesPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        int[] supervisorIds = Array.Empty<int>();
        if (SearchText.Terms(search).Count > 0)
        {
            var sageKey = (sage ?? string.Empty).Trim().ToLowerInvariant();
            var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(
                c => c.SAGE_BDD == null
                     || c.SAGE_BDD.Trim() == ""
                     || c.SAGE_BDD.Trim().ToLower() == sageKey,
                ct);
            supervisorIds = cards
                .Where(c => SearchText.Matches(search, c.SAGE_MATRICULE, c.SAGE_NOM, c.SAGE_PRENOM))
                .Select(c => c.ID)
                .ToArray();
        }

        var searchPredicate = SearchExpression.For<T_CATEGORIE>(search, c => c.INTITULE);
        System.Linq.Expressions.Expression<Func<T_CATEGORIE, bool>> predicate =
            c => c.BDD_SAGE == sage;
        if (supervisorIds.Length > 0)
        {
            System.Linq.Expressions.Expression<Func<T_CATEGORIE, bool>> supervisorPredicate =
                c => supervisorIds.Contains(c.ID_CARDPAIE);
            predicate = SearchExpression.AndAlso(
                predicate,
                searchPredicate is null
                    ? supervisorPredicate
                    : SearchExpression.OrElse(searchPredicate, supervisorPredicate));
        }
        else if (searchPredicate is not null)
        {
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        }

        return await PagedLists.PageAsync(
            _uow.Repository<T_CATEGORIE>(),
            predicate,
            q => q.OrderBy(c => c.IDCATEGORIE),
            page,
            pageSize,
            c => _mapper.Map<CategoryDto>(c),
            ct);
    }

    /// <summary>
    /// Liste paginée des affectations. Sans filtre département/service, la page est découpée en SQL
    /// puis enrichie (carte + département/service) pour les seuls matricules de la page. Avec un
    /// filtre, le jeu complet est enrichi avant le découpage, comme pour une liste calculée.
    /// </summary>
    public async Task<PagedResult<AffectationRowDto>> ListAffectationsPagedAsync(
        int? categoryId, string? departement, string? service, int? page, int? pageSize,
        string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);

        if (!string.IsNullOrWhiteSpace(departement)
            || !string.IsNullOrWhiteSpace(service)
            || SearchText.Terms(search).Count > 0)
        {
            var rows = await LoadAffectationRowsAsync(categoryId, ct);
            var filtered = ApplyAffectationFilters(rows, departement, service).ToList();
            if (SearchText.Terms(search).Count > 0)
            {
                var labels = await LoadCategoryLabelsAsync(ct);
                filtered = filtered
                    .Where(row => SearchText.Matches(
                        search,
                        row.Matricule,
                        row.Nom,
                        row.Prenom,
                        row.Departement,
                        row.Service,
                        labels.GetValueOrDefault(row.CategoryId)))
                    .ToList();
            }
            return PagedLists.Page(filtered, page, pageSize);
        }

        var sage = _tenant.SageDb;
        System.Linq.Expressions.Expression<Func<T_CAT_SAL, bool>> predicate =
            a => a.BDD_SAGE == sage && (!categoryId.HasValue || a.ID_CATEG == categoryId);
        Func<IQueryable<T_CAT_SAL>, IOrderedQueryable<T_CAT_SAL>> order =
            q => q.OrderBy(a => a.MATRICULE_SAGE).ThenBy(a => a.ID);
        var result = await _uow.Repository<T_CAT_SAL>().ToPagedAsync(
            predicate, order, page, pageSize, ct);
        if (result.Total > 0 && result.Items.Count == 0 && result.Page > result.TotalPages)
            result = await _uow.Repository<T_CAT_SAL>().ToPagedAsync(
                predicate, order, result.TotalPages, result.PageSize, ct);
        var cards = await LoadCardsAsync(result.Items.Select(a => a.MATRICULE_SAGE).ToList(), ct);
        var matricules = cards.Values
            .Select(c => c.SAGE_MATRICULE?.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .ToList();
        var deptByMat = await LoadDepartementServicesAsync(matricules, ct);
        var items = result.Items.Select(a => ToAffectationRow(a, cards, deptByMat)).ToList();
        var pageEffective = PaginationRules.PageEffective(result.Total, result.Page);
        return new PagedResult<AffectationRowDto>(items, result.Total, pageEffective, result.PageSize, result.TotalPages);
    }

    /// <summary>Jeu complet enrichi, pour l'export Excel (jamais la page affichée).</summary>
    public async Task<IReadOnlyList<AffectationRowDto>> ListAllAffectationsAsync(
        int? categoryId, string? departement, string? service, CancellationToken ct = default)
    {
        var rows = await LoadAffectationRowsAsync(categoryId, ct);
        return ApplyAffectationFilters(rows, departement, service).ToList();
    }

    /// <summary>Options des filtres département/service, calculées une fois au montage de l'écran.</summary>
    public async Task<AffectationOptionsDto> GetAffectationOptionsAsync(CancellationToken ct = default)
    {
        var rows = await LoadAffectationRowsAsync(null, ct);
        var departements = rows
            .Select(r => r.Departement)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var services = rows
            .Select(r => r.Service)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AffectationOptionsDto(departements, services);
    }

    public async Task<PagedResult<ToleranceDto>> ListTolerancesPagedAsync(
        int? categoryId, int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var labels = await LoadCategoryLabelsAsync(ct);
        var list = await _uow.Repository<T_TOLERANCE>().ListAsync(
            t => !categoryId.HasValue || t.IDCATEGORIE == categoryId, ct);
        var rows = list
            .OrderBy(t => t.IDCATEGORIE)
            .ThenBy(t => t.ID)
            .Select(t => new ToleranceDto(t.ID, t.IDCATEGORIE, t.TOLERANCE, t.TYPES_TOL, t.SORTIE))
            .Where(t => SearchText.Matches(
                search,
                labels.GetValueOrDefault(t.CategoryId),
                t.Tolerance,
                t.Sortie,
                t.TypesTol ? "oui" : "non"))
            .ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    public async Task<PagedResult<HolidayDto>> ListHolidaysPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sage, ct);
        var rows = list
            .OrderBy(f => f.ID)
            .Select(_mapper.Map<HolidayDto>)
            .Where(f => SearchText.Matches(search, f.Intitule, f.Date))
            .ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    public async Task<PagedResult<MajorationDto>> ListMajorationsPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_MAJORATION>().ListAsync(_ => true, ct);
        var rows = list
            .OrderBy(m => m.Cotation)
            .ThenBy(m => m.ID)
            .Select(_mapper.Map<MajorationDto>)
            .Where(m => SearchText.Matches(search, m.Mojoration, m.Cotation))
            .ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    public async Task<PagedResult<AbsenceCodeDto>> ListAbsenceCodesPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        return await PagedLists.PageAsync(
            _uow.Repository<T_CODEABSENCE>(),
            _ => true,
            q => q.OrderBy(a => a.ID_ABSENCE),
            page,
            pageSize,
            a => new AbsenceCodeDto(a.ID_ABSENCE, a.INTITULE_ABSENCE, a.NOT_PAY),
            search,
            new System.Linq.Expressions.Expression<Func<T_CODEABSENCE, string?>>[] { a => a.INTITULE_ABSENCE },
            ct);
    }

    /// <summary>
    /// Sept catégories ordonnées selon <see cref="OvertimeCodeKeys.Defaults"/> : l'ordre métier n'est
    /// pas une colonne SQL, on pagine donc en mémoire après le tri (volume négligeable).
    /// </summary>
    public async Task<PagedResult<CodeConstanteDto>> ListCodeConstantesPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        var list = await ListCodeConstantesAsync(ct);
        var rows = list
            .Where(c => SearchText.Matches(search, c.Categorie, c.Intitule, c.CodeConstante, c.TableCible))
            .ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    private async Task<List<AffectationRowDto>> LoadAffectationRowsAsync(
        int? categoryId, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var affectations = await _uow.Repository<T_CAT_SAL>().ListAsync(
            a => a.BDD_SAGE == sage && (!categoryId.HasValue || a.ID_CATEG == categoryId), ct);

        var cards = await LoadCardsAsync(affectations.Select(a => a.MATRICULE_SAGE).ToList(), ct);
        var matricules = cards.Values
            .Select(c => c.SAGE_MATRICULE?.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .ToList();
        var deptByMat = await LoadDepartementServicesAsync(matricules, ct);
        return affectations.Select(a => ToAffectationRow(a, cards, deptByMat)).ToList();
    }

    private async Task<Dictionary<int, string>> LoadCategoryLabelsAsync(CancellationToken ct)
    {
        var sage = _tenant.SageDb;
        var categories = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == sage, ct);
        return categories.ToDictionary(
            category => category.IDCATEGORIE,
            category => category.INTITULE ?? "Catégorie introuvable");
    }

    private async Task<Dictionary<int, T_CARDPAIE>> LoadCardsAsync(
        IReadOnlyCollection<int> cardIds, CancellationToken ct)
    {
        var result = new Dictionary<int, T_CARDPAIE>();
        foreach (var chunk in cardIds.Distinct().Chunk(500))
        {
            var ids = chunk.ToList();
            var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => ids.Contains(c.ID), ct);
            foreach (var card in cards) result[card.ID] = card;
        }
        return result;
    }

    private async Task<Dictionary<string, DepartementServiceDto>> LoadDepartementServicesAsync(
        IReadOnlyCollection<string> matricules, CancellationToken ct)
    {
        var result = new Dictionary<string, DepartementServiceDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in matricules
                     .Where(m => !string.IsNullOrWhiteSpace(m))
                     .Select(m => m.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Chunk(2000))
        {
            try
            {
                var rows = await GetDepartementServiceBatchAsync(chunk, ct);
                foreach (var row in rows) result[row.Matricule.Trim()] = row;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Source RH/paie indisponible : on laisse département/service vides plutôt que
                // de faire échouer toute la page (même tolérance que l'ancien enrichissement front).
            }
        }
        return result;
    }

    private static AffectationRowDto ToAffectationRow(
        T_CAT_SAL affectation,
        IReadOnlyDictionary<int, T_CARDPAIE> cards,
        IReadOnlyDictionary<string, DepartementServiceDto> deptByMat)
    {
        cards.TryGetValue(affectation.MATRICULE_SAGE, out var card);
        var matricule = card?.SAGE_MATRICULE?.Trim();
        deptByMat.TryGetValue(matricule ?? string.Empty, out var dept);
        return new AffectationRowDto(
            affectation.ID,
            affectation.MATRICULE_SAGE,
            affectation.ID_CATEG,
            matricule,
            card?.SAGE_NOM,
            card?.SAGE_PRENOM,
            dept?.Departement,
            dept?.Service);
    }

    private static IEnumerable<AffectationRowDto> ApplyAffectationFilters(
        IEnumerable<AffectationRowDto> rows, string? departement, string? service)
    {
        var dept = string.IsNullOrWhiteSpace(departement) ? null : departement.Trim();
        var serv = string.IsNullOrWhiteSpace(service) ? null : service.Trim();
        return rows.Where(r =>
            (dept is null || string.Equals(r.Departement, dept, StringComparison.OrdinalIgnoreCase))
            && (serv is null || string.Equals(r.Service, serv, StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>
    /// Supprime la correspondance SAGE ↔ badge identifiée par le couple de matricules
    /// (portage de <c>SupprimerParCorrespondance</c> du desktop). La comparaison des matricules est
    /// insensible au remplissage par des zéros. Le badge est optionnel : sans lui, toutes les
    /// correspondances du matricule SAGE du tenant courant sont supprimées.
    /// </summary>
    public async Task<int> DeleteCardByCorrespondenceAsync(string matriculeSage, string? matriculePointeuse, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (string.IsNullOrWhiteSpace(matriculeSage))
            throw new InvalidOperationException("Matricule SAGE requis.");
        var sage = _tenant.SageDb;
        var repo = _uow.Repository<T_CARDPAIE>();
        var candidates = (await repo.ListAsync(c =>
                c.SAGE_BDD == sage && c.SAGE_MATRICULE == matriculeSage, ct))
            .Where(c => MatriculeIdentity.MemeSalarie(c.SAGE_MATRICULE, matriculeSage))
            .Where(c => string.IsNullOrWhiteSpace(matriculePointeuse)
                        || MatriculeIdentity.MemeSalarie(c.POINTEUSE_NUMERO, matriculePointeuse))
            .ToList();
        if (candidates.Count == 0)
            throw new KeyNotFoundException($"Aucune correspondance {matriculeSage} ↔ {matriculePointeuse ?? "*"} sur la base {sage}.");
        repo.RemoveRange(candidates);
        await _uow.SaveChangesAsync(ct);
        return candidates.Count;
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
                // En-tête structurel : ignoré, mais pas compté comme une ligne de données ignorée.
                continue;
            }
            if (string.IsNullOrWhiteSpace(matricule))
            {
                skipped++;
                continue;
            }
            if (ExcelCells.Fold(matricule) is "MATRICULE" or "MATRICULESAGE")
            {
                // En-tête décalé (colonne matricule) : même traitement que l'en-tête.
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
        return new ImportResultDto(imported, skipped,
            $"{imported} correspondance(s) importée(s), {skipped} ligne(s) ignorée(s).");
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
        var lignesVides = 0;
        var categoriesManquantes = 0;
        var matriculesManquants = 0;
        var categoriesIntrouvables = new List<string>();
        var matriculesIntrouvables = new List<string>();
        foreach (var row in rows)
        {
            var catName = ExcelCells.Get(row, "Categorie", "CATEGORIE", "Intitule", "Intitulé")
                        ?? row.Values.FirstOrDefault();
            var mat = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE")
                    ?? (row.Count > 1 ? row.Values.Skip(1).FirstOrDefault() : null);
            if (string.IsNullOrWhiteSpace(catName) || string.IsNullOrWhiteSpace(mat))
            {
                lignesVides++;
                skipped++;
                continue;
            }
            var cat = cats.FirstOrDefault(c => string.Equals(c.INTITULE?.Trim(), catName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cat == null)
            {
                categoriesManquantes++;
                AjouterValeurDistincte(categoriesIntrouvables, catName);
                skipped++;
                continue;
            }
            var card = cards.FirstOrDefault(c => string.Equals(c.SAGE_MATRICULE?.Trim(), mat.Trim(), StringComparison.OrdinalIgnoreCase));
            if (card == null)
            {
                matriculesManquants++;
                AjouterValeurDistincte(matriculesIntrouvables, mat);
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
        var raisons = new List<string>();
        if (lignesVides > 0)
            raisons.Add($"{lignesVides} ligne(s) vide(s)");
        if (categoriesManquantes > 0)
            raisons.Add($"{categoriesManquantes} catégorie(s) introuvable(s) : {ApercuValeurs(categoriesIntrouvables)}");
        if (matriculesManquants > 0)
            raisons.Add($"{matriculesManquants} matricule(s) introuvable(s) : {ApercuValeurs(matriculesIntrouvables)}");
        var message = $"{imported} affectation(s) importée(s), {skipped} ligne(s) ignorée(s)";
        if (raisons.Count > 0)
            message += $" ({string.Join(" ; ", raisons)})";
        return new ImportResultDto(imported, skipped, message + ".");
    }

    /// <summary>Ajoute une valeur à afficher dans le bilan, sans doublon (comparaison insensible à la casse).</summary>
    private static void AjouterValeurDistincte(List<string> valeurs, string valeur)
    {
        var propre = valeur.Trim();
        if (!valeurs.Contains(propre, StringComparer.OrdinalIgnoreCase))
            valeurs.Add(propre);
    }

    /// <summary>Liste les premières valeurs fautives pour le message d'import.</summary>
    private static string ApercuValeurs(IReadOnlyList<string> valeurs)
        => string.Join(", ", valeurs.Take(5)) + (valeurs.Count > 5 ? ", …" : string.Empty);

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

    public async Task DeleteToleranceAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_TOLERANCE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
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
        // Sur une base AUTRE, la table cible provient du mapping « Code constante » : on l'expose
        // pour que l'ecran pre-selectionne la bonne table (le code reste global, comme dans la refonte).
        string? mappedTable = null;
        var database = await _tenant.GetSageRowAsync(ct);
        if (string.Equals(database.TYPE_BASE, "AUTRE", StringComparison.OrdinalIgnoreCase))
        {
            try { mappedTable = (await GetOtherCodeConstantMappingAsync(ct)).Table; }
            catch (InvalidOperationException) { mappedTable = null; }
        }
        return list
            .OrderBy(c => order.TryGetValue(c.CATEGORIE, out var i) ? i : 99)
            .Select(c => _mapper.Map<CodeConstanteDto>(c) with { TableCible = mappedTable })
            .ToList();
    }

    public async Task<CodeConstanteDto> SaveCodeConstanteAsync(CodeConstanteDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var code = (dto.CodeConstante ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Le code constante SAGE est requis.");
        var database = await _tenant.GetSageRowAsync(ct);
        if (string.Equals(database.TYPE_BASE, "AUTRE", StringComparison.OrdinalIgnoreCase))
        {
            // Herite de la fonctionnalite « code constante » de la base AUTRE : le code choisi doit
            // exister dans la colonne mappee (role SAGE_CST_CODE) de la table cible.
            var values = await ListMappedCodeConstantValuesAsync(ct);
            if (!values.Contains(code, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Le code constante sélectionné n'existe pas dans la table mappée.");
        }
        var repo = _uow.Repository<T_CODE_CONSTANTE>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
        entity.CODE_CONSTANTE = code;
        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CodeConstanteDto>(entity) with { TableCible = dto.TableCible };
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
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'UX_T_CODE_CONSTANTE_GLOBAL' AND object_id = OBJECT_ID('dbo.T_CODE_CONSTANTE'))
                    DROP INDEX UX_T_CODE_CONSTANTE_GLOBAL ON dbo.T_CODE_CONSTANTE;
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'UX_T_CODE_CONSTANTE_BASE' AND object_id = OBJECT_ID('dbo.T_CODE_CONSTANTE'))
                    DROP INDEX UX_T_CODE_CONSTANTE_BASE ON dbo.T_CODE_CONSTANTE;
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