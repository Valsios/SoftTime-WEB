using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

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
        if (dto.Id == 0)
            await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Create, dto.NomBd, ct);
        else
        {
            var current = await _uow.Repository<BddPointeuse>().GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Update, current.NomBd, ct);
        }
        if (dto.TypeBase != "STANDARD" && dto.TypeBase != "AUTRE")
            throw new DomainException("TypeBase doit valoir 'STANDARD' ou 'AUTRE'.");
        if (dto.TypeBase == "AUTRE" && (dto.Mappings is null || dto.Mappings.Count == 0))
            throw new DomainException("Une base 'Autre' nécessite au minimum un mapping de source.");
        await _sourceMappingMigrator.EnsureMappingSchemaAsync(ct);
        var repo = _uow.Repository<BddPointeuse>();
        var doublon = (await repo.ListAsync(
            p => p.Id != dto.Id
                 && p.Serveur == dto.Serveur
                 && p.NomBd == dto.NomBd, ct)).Any();
        if (doublon)
            throw new DomainException("Cette base (serveur + nom) est déjà enregistrée.");
        if (dto.Active == true)
        {
            var all = await repo.ListAsync(_ => true, ct);
            foreach (var p in all)
            {
                p.Active = p.Id == dto.Id;
                repo.Update(p);
            }
        }
        BddPointeuse entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<BddPointeuse>(dto);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            entity.Serveur = dto.Serveur;
            entity.TLogin = dto.Login;
            if (!string.IsNullOrEmpty(dto.Password)) entity.TMdp = dto.Password;
            entity.TypeAuth = dto.SqlAuth;
            entity.NomBd = dto.NomBd;
            entity.TypePointage = dto.TypePointage;
            entity.Active = dto.Active;
            entity.TypeBase = dto.TypeBase;
            repo.Update(entity);
        }
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

    private static readonly string[] PointeuseStandardTables = { "USERINFO", "CHECKINOUT" };

    public async Task<ConnectionTestResultDto> TestPointeuseConnectionAsync(TestPointeuseConnectionDto dto, CancellationToken ct = default)
    {
        var password = await _sourceMappingStore.ResolveStoredPasswordAsync("POINTEUSE", dto.Id, dto.Serveur, dto.SqlAuth, dto.Login, dto.Password, ct);
        if (string.IsNullOrWhiteSpace(dto.Serveur) || string.IsNullOrWhiteSpace(dto.NomBd))
            return new ConnectionTestResultDto(false, "Serveur et nom de base requis.");
        IReadOnlyList<string> tables;
        try
        {
            tables = await _discovery.ListTablesAsync(dto.Serveur, dto.NomBd, dto.SqlAuth ?? true, dto.Login, password, ct);
        }
        catch (ExternalConnectionException ex)
        {
            // Message lisible, sans texte SQL (voir ExternalDiscoveryService.Describe) ; toute autre erreur va au middleware (500 journalisé).
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
                    var missingCols = await _sourceMappingValidator.ValidateMappingColumnsAsync(dto.Serveur!, dto.NomBd!, dto.SqlAuth ?? true, dto.Login, password, dto.Mappings, ct);
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
        var searchPredicate = SearchExpression.For<BddPointeuse>(
            search, p => p.Serveur, p => p.NomBd, p => p.TypeBase);
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        var result = await _uow.Repository<BddPointeuse>().ToPagedAsync(
            predicate, q => q.OrderBy(p => p.Id), page, pageSize, ct);
        if (result.Total > 0 && result.Items.Count == 0 && result.Page > result.TotalPages)
            result = await _uow.Repository<BddPointeuse>().ToPagedAsync(
                predicate, q => q.OrderBy(p => p.Id), result.TotalPages, result.PageSize, ct);
        var mappings = await _sourceMappingStore.LoadMappingLookupAsync(false, result.Items.Select(p => p.Id).ToList(), ct);
        var items = result.Items.Select(p => _mapper.Map<PointeuseDbDto>(p) with
        {
            Mappings = mappings.TryGetValue(p.Id, out var m) ? m : Array.Empty<SourceEntityMappingDto>()
        }).ToList();
        var pageEffective = PaginationRules.PageEffective(result.Total, result.Page);
        return new PagedResult<PointeuseDbDto>(items, result.Total, pageEffective, result.PageSize, result.TotalPages);
    }
}
