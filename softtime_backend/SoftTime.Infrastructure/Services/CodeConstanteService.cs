using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class CodeConstanteService : ICodeConstanteService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly IExternalDiscoveryService _discovery;

    public CodeConstanteService(
        IUnitOfWork uow,
        IMapper mapper,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IExternalDiscoveryService discovery)
    {
        _uow = uow;
        _mapper = mapper;
        _tenant = tenant;
        _externalSource = externalSource;
        _discovery = discovery;
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
        SourceIdentifiers.ValidateSourceIdentifier(mappedTable);
        SourceIdentifiers.ValidateSourceIdentifier(column);
        return await _discovery.ListDistinctValuesAsync(database.Serveur!, database.NomBd!, mappedTable, column,
            database.TypeAuth == true, database.TLogin, database.TMdp, ct);
    }

    // Resout la table/colonne du code constante depuis le mapping de l'entite CONSTANT.
    private async Task<(string Table, string Column)> GetOtherCodeConstantMappingAsync(CancellationToken ct)
    {
        var database = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Cette liste est réservée aux bases AUTRE.");
        var entity = (await _uow.Repository<SourceEntityMapping>()
                .ListAsync(m => m.SageDbId == database.Id && m.EntityKind == "CONSTANT", ct))
            .FirstOrDefault();
        if (entity is null || string.IsNullOrWhiteSpace(entity.SourceTable))
            throw new DomainException("Configurez d'abord le mapping « Code constante » (table et colonne) dans Bases RH / paie.");
        var field = (await _uow.Repository<SourceFieldMapping>()
                .ListAsync(f => f.EntityMappingId == entity.Id && f.FieldRoleCode == "SAGE_CST_CODE", ct))
            .FirstOrDefault();
        if (field is null || string.IsNullOrWhiteSpace(field.SourceColumn))
            throw new DomainException("La colonne de code constante n'est pas mappée (rôle SAGE_CST_CODE).");
        return (entity.SourceTable!.Trim(), field.SourceColumn!.Trim());
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

    public async Task<IReadOnlyList<CodeConstanteDto>> ListCodeConstantesAsync(CancellationToken ct = default)
    {
        await EnsureCodeConstantesAsync(ct);
        var list = await _uow.Repository<CodeConstanteCategorie>().ListAsync(_ => true, ct);
        var order = OvertimeCodeKeys.Defaults.Select((d, i) => (d.Key, i)).ToDictionary(x => x.Key, x => x.i);
        // Sur une base AUTRE, la table cible provient du mapping « Code constante » : on l'expose
        // pour que l'ecran pre-selectionne la bonne table (le code reste global, comme dans la refonte).
        string? mappedTable = null;
        var database = await _tenant.GetSageRowAsync(ct);
        if (string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase))
        {
            try { mappedTable = (await GetOtherCodeConstantMappingAsync(ct)).Table; }
            catch (DomainException) { mappedTable = null; }
        }
        return list
            .OrderBy(c => order.TryGetValue(c.Categorie, out var i) ? i : 99)
            .Select(c => _mapper.Map<CodeConstanteDto>(c) with { TableCible = mappedTable })
            .ToList();
    }

    public async Task<CodeConstanteDto> SaveCodeConstanteAsync(CodeConstanteDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var code = (dto.CodeConstante ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Le code constante SAGE est requis.");
        var database = await _tenant.GetSageRowAsync(ct);
        if (string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase))
        {
            // Herite de la fonctionnalite « code constante » de la base AUTRE : le code choisi doit
            // exister dans la colonne mappee (role SAGE_CST_CODE) de la table cible.
            var values = await ListMappedCodeConstantValuesAsync(ct);
            if (!values.Contains(code, StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Le code constante sélectionné n'existe pas dans la table mappée.");
        }
        var repo = _uow.Repository<CodeConstanteCategorie>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
        entity.CodeConstante = code;
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
        var list = await _uow.Repository<CodeConstanteCategorie>().ListAsync(_ => true, ct);
        string Code(string key, string fallback) =>
            list.FirstOrDefault(c => c.Categorie == key)?.CodeConstante is { Length: > 0 } v ? v.Trim() : fallback;

        return new OvertimeSageCodes(
            Code(OvertimeCodeKeys.Exo130, "HS01"),
            Code(OvertimeCodeKeys.Exo150, "HS02"),
            Code(OvertimeCodeKeys.I130, "HS03"),
            Code(OvertimeCodeKeys.I150, "HS04"),
            Code(OvertimeCodeKeys.Ferie, "HS05"),
            Code(OvertimeCodeKeys.Dim, "HS06"),
            Code(OvertimeCodeKeys.Nuit, "HS07"));
    }

    public async Task EnsureCodeConstantesAsync(CancellationToken ct)
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

        var repo = _uow.Repository<CodeConstanteCategorie>();
        var existing = await repo.ListAsync(_ => true, ct);
        var added = false;
        foreach (var (key, intitule, defaultCode) in OvertimeCodeKeys.Defaults)
        {
            if (existing.Any(c => c.Categorie == key))
                continue;
            await repo.AddAsync(new CodeConstanteCategorie
            {
                Categorie = key,
                Intitule = intitule,
                CodeConstante = defaultCode,
            }, ct);
            added = true;
        }
        if (added)
            await _uow.SaveChangesAsync(ct);
    }
}
