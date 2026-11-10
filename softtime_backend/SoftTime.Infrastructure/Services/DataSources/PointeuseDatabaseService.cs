using AutoMapper;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.DataSources;

public sealed class PointeuseDatabaseService : IPointeuseDatabaseService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IExternalDiscoveryService _discovery;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly ISourceMappingStore _sourceMappingStore;
    private readonly ISourceMappingMigrator _sourceMappingMigrator;
    private readonly ISourceMappingValidator _sourceMappingValidator;

    public PointeuseDatabaseService(
        IUnitOfWork uow,
        IMapper mapper,
        IExternalDiscoveryService discovery,
        IDatabaseConfigurationAuthorizer configAuth,
        IEffectiveRightsService effectiveRights,
        ISourceMappingStore sourceMappingStore,
        ISourceMappingMigrator sourceMappingMigrator,
        ISourceMappingValidator sourceMappingValidator)
    {
        _uow = uow;
        _mapper = mapper;
        _discovery = discovery;
        _configAuth = configAuth;
        _effectiveRights = effectiveRights;
        _sourceMappingStore = sourceMappingStore;
        _sourceMappingMigrator = sourceMappingMigrator;
        _sourceMappingValidator = sourceMappingValidator;
    }

    public async Task<IReadOnlyList<PointeuseDbDto>> ListPointeuseAsync(CancellationToken ct = default)
    {
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        await _sourceMappingMigrator.MigrateLegacyMappingsAsync(ct);
        var rows = await _uow.Repository<BddPointeuse>().ListAsync(_ => true, ct);
        var byConnection = await _sourceMappingStore.LoadMappingLookupAsync(false, ct);
        return rows.Select(r => _mapper.Map<PointeuseDbDto>(r) with
        {
            Mappings = byConnection.TryGetValue(r.Id, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
    }

    public async Task<PointeuseDbDto?> GetPointeuseByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<BddPointeuse>().GetByIdAsync(id, ct);
        return entity == null ? null : _mapper.Map<PointeuseDbDto>(entity);
    }

    public async Task<PointeuseDbDto> SavePointeuseAsync(PointeuseDbDto dto, CancellationToken ct = default)
    {
        await AuthorizeSaveAsync(dto, ct);
        DatabaseTypeBase.Validate(dto.TypeBase, dto.Mappings?.Count ?? 0);
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        var repo = _uow.Repository<BddPointeuse>();
        var doublon = (await repo.ListAsync(
            p => p.Id != dto.Id && p.Serveur == dto.Serveur && p.NomBd == dto.NomBd, ct)).Any();
        if (doublon)
            throw new ConflictException(DatabaseTypeBase.DoublonMessage);
        if (dto.Active == true)
            await ActiverSeuleAsync(dto.Id, ct);

        var entity = await UpsertAsync(dto, ct);
        await _uow.SaveChangesAsync(ct);
        _effectiveRights.InvalidateAll(); // la liste des bases a changé : les droits effectifs en cache sont périmés
        if (dto.Mappings is not null)
        {
            await _sourceMappingValidator.ValidateMappingsAsync(false, dto.Mappings, ct);
            await _sourceMappingStore.ReplaceMappingsAsync(false, entity.Id, dto.Mappings, ct);
        }
        var result = _mapper.Map<PointeuseDbDto>(entity);
        return result with { Mappings = await _sourceMappingStore.LoadMappingsForAsync(false, entity.Id, ct) };
    }

    private async Task AuthorizeSaveAsync(PointeuseDbDto dto, CancellationToken ct)
    {
        if (dto.Id == 0)
        {
            await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Create, dto.NomBd, ct);
            return;
        }
        var current = await _uow.Repository<BddPointeuse>().GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
        await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Update, current.NomBd, ct);
    }

    /// <summary>Une seule pointeuse active : celle enregistrée.</summary>
    private async Task ActiverSeuleAsync(int id, CancellationToken ct)
    {
        var repo = _uow.Repository<BddPointeuse>();
        foreach (var p in await repo.ListAsync(_ => true, ct))
        {
            p.Active = p.Id == id;
            repo.Update(p);
        }
    }

    private async Task<BddPointeuse> UpsertAsync(PointeuseDbDto dto, CancellationToken ct)
    {
        var repo = _uow.Repository<BddPointeuse>();
        if (dto.Id == 0)
        {
            var created = _mapper.Map<BddPointeuse>(dto);
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
        entity.TypePointage = dto.TypePointage;
        entity.Active = dto.Active;
        entity.TypeBase = dto.TypeBase;
        repo.Update(entity);
        return entity;
    }

    private static readonly string[] PointeuseStandardTables = { "USERINFO", "CHECKINOUT" };

    public async Task<ConnectionTestResultDto> TestPointeuseConnectionAsync(TestPointeuseConnectionDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync(new ConnectionAttempt("POINTEUSE", dto.Id, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password), ct);
        if (string.IsNullOrWhiteSpace(dto.Serveur) || string.IsNullOrWhiteSpace(dto.NomBd))
            return new ConnectionTestResultDto(false, "Serveur et nom de base requis.");
        var cible = new ExternalConnectionTarget(new SqlServerCredentials(dto.Serveur, dto.SqlAuth ?? true, dto.Login, password), dto.NomBd, dto.TypeBase, dto.Mappings);
        var standard = new StandardStructure(PointeuseStandardTables, "Connexion réussie, structure pointeuse standard reconnue.", "Connexion réussie mais les tables USERINFO/CHECKINOUT sont introuvables.");
        return await new ExternalConnectionTester(_discovery, _sourceMappingValidator).TestAsync(cible, standard, ct);
    }

    public async Task DeletePointeuseAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<BddPointeuse>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, e.NomBd, ct);
        await _sourceMappingStore.RemoveMappingsAsync(false, id, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
        _effectiveRights.InvalidateAll(); // la liste des bases a changé : les droits effectifs en cache sont périmés
    }

    public async Task<PagedResult<PointeuseDbDto>> ListPointeusePagedAsync(
        IReadOnlyCollection<string>? allowedNames, int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        await _sourceMappingMigrator.MigrateLegacyMappingsAsync(ct);

        if (allowedNames is { Count: 0 })
        {
            var (_, taille) = PaginationRules.Normaliser(page, pageSize);
            return new PagedResult<PointeuseDbDto>(Array.Empty<PointeuseDbDto>(), 0, 1, taille, 1);
        }

        System.Linq.Expressions.Expression<Func<BddPointeuse, bool>> predicate = allowedNames is null
            ? p => true
            : p => p.NomBd != null && allowedNames.Contains(p.NomBd);
        var result = await PagedLists.From(_uow.Repository<BddPointeuse>(), predicate, q => q.OrderBy(p => p.Id))
            .WithSearch(search, new System.Linq.Expressions.Expression<Func<BddPointeuse, string?>>[] { p => p.Serveur, p => p.NomBd, p => p.TypeBase })
            .PageAsync(new PageRequest(page, pageSize), p => p, ct);
        var mappings = await _sourceMappingStore.LoadMappingLookupAsync(false, result.Items.Select(p => p.Id).ToList(), ct);
        var items = result.Items.Select(p => _mapper.Map<PointeuseDbDto>(p) with
        {
            Mappings = mappings.TryGetValue(p.Id, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
        return new PagedResult<PointeuseDbDto>(items, result.Total, result.Page, result.PageSize, result.TotalPages);
    }
}
