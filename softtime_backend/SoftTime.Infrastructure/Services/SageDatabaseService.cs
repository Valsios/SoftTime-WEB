using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class SageDatabaseService : ISageDatabaseService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalDiscoveryService _discovery;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IHolidayService _holidayService;
    private readonly ICodeConstanteService _codeConstanteService;
    private readonly ICardPaieService _cardPaieService;
    private readonly ISourceMappingStore _sourceMappingStore;
    private readonly ISourceMappingMigrator _sourceMappingMigrator;
    private readonly ISourceMappingValidator _sourceMappingValidator;

    public SageDatabaseService(
        IUnitOfWork uow,
        IMapper mapper,
        ITenantConnectionService tenant,
        IExternalDiscoveryService discovery,
        IDatabaseConfigurationAuthorizer configAuth,
        IEffectiveRightsService effectiveRights,
        IHolidayService holidayService,
        ICodeConstanteService codeConstanteService,
        ICardPaieService cardPaieService,
        ISourceMappingStore sourceMappingStore,
        ISourceMappingMigrator sourceMappingMigrator,
        ISourceMappingValidator sourceMappingValidator)
    {
        _uow = uow;
        _mapper = mapper;
        _tenant = tenant;
        _discovery = discovery;
        _configAuth = configAuth;
        _effectiveRights = effectiveRights;
        _holidayService = holidayService;
        _codeConstanteService = codeConstanteService;
        _cardPaieService = cardPaieService;
        _sourceMappingStore = sourceMappingStore;
        _sourceMappingMigrator = sourceMappingMigrator;
        _sourceMappingValidator = sourceMappingValidator;
    }

    public async Task<IReadOnlyList<SageDbDto>> ListSageAsync(CancellationToken ct = default)
    {
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        await _sourceMappingMigrator.MigrateLegacyMappingsAsync(ct);
        var rows = await _uow.Repository<BddSage>().ListAsync(_ => true, ct);
        var byConnection = await _sourceMappingStore.LoadMappingLookupAsync(true, ct);
        return rows.Select(r => _mapper.Map<SageDbDto>(r) with
        {
            Mappings = byConnection.TryGetValue(r.Id, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
    }

    public async Task<SageDbDto?> GetSageByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<BddSage>().GetByIdAsync(id, ct);
        return entity == null ? null : _mapper.Map<SageDbDto>(entity);
    }

    public async Task<SageDbDto> SaveSageAsync(SageDbDto dto, CancellationToken ct = default)
    {
        await AuthorizeSaveAsync(dto, ct);
        DatabaseTypeBase.Validate(dto.TypeBase, dto.Mappings?.Count ?? 0);
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        var doublon = (await _uow.Repository<BddSage>().ListAsync(
            s => s.Id != dto.Id && s.Serveur == dto.Serveur && s.NomBd == dto.NomBd, ct)).Any();
        if (doublon)
            throw new DomainException(DatabaseTypeBase.DoublonMessage);

        var entity = await UpsertAsync(dto, ct);
        await _uow.SaveChangesAsync(ct);
        _effectiveRights.InvalidateAll(); // la liste des bases a changé : les droits effectifs en cache sont périmés
        if (dto.Mappings is not null)
        {
            await _sourceMappingValidator.ValidateMappingsAsync(true, dto.Mappings, ct);
            await _sourceMappingStore.ReplaceMappingsAsync(true, entity.Id, dto.Mappings, ct);
        }
        var result = _mapper.Map<SageDbDto>(entity);
        return result with { Mappings = await _sourceMappingStore.LoadMappingsForAsync(true, entity.Id, ct) };
    }

    private async Task AuthorizeSaveAsync(SageDbDto dto, CancellationToken ct)
    {
        if (dto.Id == 0)
        {
            await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, dto.NomBd, ct);
            return;
        }
        var current = await _uow.Repository<BddSage>().GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
        await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, current.NomBd, ct);
    }

    private async Task<BddSage> UpsertAsync(SageDbDto dto, CancellationToken ct)
    {
        var repo = _uow.Repository<BddSage>();
        if (dto.Id == 0)
        {
            var created = _mapper.Map<BddSage>(dto);
            await repo.AddAsync(created, ct);
            return created;
        }
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
        entity.Serveur = dto.Serveur;
        entity.TLogin = dto.Login;
        if (!string.IsNullOrEmpty(dto.Password))
            entity.TMdp = dto.Password;
        entity.TypeAuth = dto.SqlAuth;
        entity.NomBd = dto.NomBd;
        entity.TypeBase = dto.TypeBase;
        repo.Update(entity);
        return entity;
    }

    // Verifie la connexion et, en mode STANDARD, la presence des tables attendues par le reste de l'application.
    private static readonly string[] SageStandardTables = { "T_SAL", "T_HST_AFFECTATION", "T_DEPARTEMENT", "T_GHRCAL_SOCIETE", "T_CST" };

    public async Task<ConnectionTestResultDto> TestSageConnectionAsync(TestSageConnectionDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync("SAGE", dto.Id, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password, ct);
        if (string.IsNullOrWhiteSpace(dto.Serveur) || string.IsNullOrWhiteSpace(dto.NomBd))
            return new ConnectionTestResultDto(false, "Serveur et nom de base requis.");
        var cible = new ExternalConnectionTarget(new SqlServerCredentials(dto.Serveur, dto.SqlAuth ?? true, dto.Login, password), dto.NomBd, dto.TypeBase, dto.Mappings);
        var standard = new StandardStructure(SageStandardTables, "Connexion réussie, structure Sage standard reconnue.", "Connexion réussie mais certaines tables Sage attendues sont introuvables.");
        return await new ExternalConnectionTester(_discovery, _sourceMappingValidator).TestAsync(cible, standard, ct);
    }

    public async Task DeleteSageAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<BddSage>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Delete, e.NomBd, ct);
        await _sourceMappingStore.RemoveMappingsAsync(true, id, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
        _effectiveRights.InvalidateAll(); // la liste des bases a changé : les droits effectifs en cache sont périmés
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
        if (!string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase))
            return new DepartementServiceOptionsDto(Array.Empty<string>(), Array.Empty<string>());

        var entity = (await _uow.Repository<SourceEntityMapping>()
                .ListReadOnlyAsync(m => m.SageDbId == database.Id && m.EntityKind == "AFFECTATION", ct))
            .FirstOrDefault();
        if (entity is null || string.IsNullOrWhiteSpace(entity.SourceTable))
            return new DepartementServiceOptionsDto(Array.Empty<string>(), Array.Empty<string>());

        var fields = await _uow.Repository<SourceFieldMapping>()
            .ListReadOnlyAsync(f => f.EntityMappingId == entity.Id, ct);
        string? ColumnOf(string role) => fields
            .FirstOrDefault(f => string.Equals(f.FieldRoleCode, role, StringComparison.OrdinalIgnoreCase))
            ?.SourceColumn?.Trim();

        async Task<IReadOnlyList<string>> ValuesAsync(string role)
        {
            var column = ColumnOf(role);
            if (string.IsNullOrWhiteSpace(column))
                return Array.Empty<string>();
            SourceIdentifiers.ValidateSourceIdentifier(entity.SourceTable);
            SourceIdentifiers.ValidateSourceIdentifier(column);
            return await _discovery.ListDistinctValuesAsync(
                SqlServerCredentials.Of(database),
                database.NomBd!,
                entity.SourceTable!,
                column!,
                ct);
        }

        return new DepartementServiceOptionsDto(
            await ValuesAsync("SAGE_AFF_DEPARTEMENT"),
            await ValuesAsync("SAGE_AFF_SERVICE"));
    }

    // ---------------------------------------------------------------- Listes paginées (Lot A)

    public async Task<PagedResult<SageDbDto>> ListSagePagedAsync(
        IReadOnlyCollection<string>? allowedNames, int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        await _sourceMappingMigrator.MigrateLegacyMappingsAsync(ct);

        if (allowedNames is { Count: 0 })
        {
            var (_, taille) = PaginationRules.Normaliser(page, pageSize);
            return new PagedResult<SageDbDto>(Array.Empty<SageDbDto>(), 0, 1, taille, 1);
        }

        System.Linq.Expressions.Expression<Func<BddSage, bool>> predicate = allowedNames is null
            ? s => true
            : s => s.NomBd != null && allowedNames.Contains(s.NomBd);
        var result = await PagedLists.From(_uow.Repository<BddSage>(), predicate, q => q.OrderBy(s => s.Id))
            .WithSearch(search, new System.Linq.Expressions.Expression<Func<BddSage, string?>>[] { s => s.Serveur, s => s.NomBd, s => s.TypeBase })
            .PageAsync(new PageRequest(page, pageSize), s => s, ct);
        var mappings = await _sourceMappingStore.LoadMappingLookupAsync(true, result.Items.Select(s => s.Id).ToList(), ct);
        var items = result.Items.Select(s => _mapper.Map<SageDbDto>(s) with
        {
            Mappings = mappings.TryGetValue(s.Id, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
        return new PagedResult<SageDbDto>(items, result.Total, result.Page, result.PageSize, result.TotalPages);
    }

    
    public async Task<ActivationResultDto> ActivateSagePairAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);

        var holidaysImported = false;
        if (sageRow.TypeBase == "STANDARD")
        {
            await _holidayService.ImportSageHolidaysAsync(ct);
            holidaysImported = true;
        }

        await _codeConstanteService.EnsureCodeConstantesAsync(ct);
        var sync = await _cardPaieService.AutoMapCardsAsync(ct);

        return new ActivationResultDto(holidaysImported, sync.Added, sync.Deactivated, sync.Message);
    }
}
