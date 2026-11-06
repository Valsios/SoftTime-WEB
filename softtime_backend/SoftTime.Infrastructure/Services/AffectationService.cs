using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class AffectationService : IAffectationService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ICategoryService _categoryService;
    private readonly IDepartementServiceLookup _departementServiceLookup;

    public AffectationService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ICategoryService categoryService,
        IDepartementServiceLookup departementServiceLookup)
    {
        _uow = uow;
        _tenant = tenant;
        _categoryService = categoryService;
        _departementServiceLookup = departementServiceLookup;
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
        // Département, service et recherche ne sont connus qu'après lecture SAGE : filtrage en mémoire.
        if (!string.IsNullOrWhiteSpace(departement)
            || !string.IsNullOrWhiteSpace(service)
            || SearchText.Terms(search).Count > 0)
        {
            var filtered = ApplyAffectationFilters(await LoadAffectationRowsAsync(categoryId, ct), departement, service).ToList();
            if (SearchText.Terms(search).Count > 0)
                filtered = await FilterBySearchAsync(filtered, search, ct);
            return PagedLists.Page(filtered, page, pageSize);
        }
        return await ListSqlPagedAsync(categoryId, page, pageSize, ct);
    }

    private async Task<List<AffectationRowDto>> FilterBySearchAsync(List<AffectationRowDto> rows, string? search, CancellationToken ct)
    {
        var labels = await _categoryService.LoadCategoryLabelsAsync(ct);
        return rows
            .Where(row => SearchText.Matches(
                search, row.Matricule, row.Nom, row.Prenom, row.Departement, row.Service,
                labels.GetValueOrDefault(row.CategoryId)))
            .ToList();
    }

    /// <summary>Pagination SQL des affectations, enrichies ensuite des cartes et départements SAGE de la page.</summary>
    private async Task<PagedResult<AffectationRowDto>> ListSqlPagedAsync(int? categoryId, int? page, int? pageSize, CancellationToken ct)
    {
        var sage = _tenant.SageDb;
        System.Linq.Expressions.Expression<Func<CategorieSalarie, bool>> predicate =
            a => a.BddSage == sage && (!categoryId.HasValue || a.IdCateg == categoryId);
        Func<IQueryable<CategorieSalarie>, IOrderedQueryable<CategorieSalarie>> order =
            q => q.OrderBy(a => a.MatriculeSage).ThenBy(a => a.Id);
        var result = await _uow.Repository<CategorieSalarie>().ToPagedAsync(predicate, order, page, pageSize, ct);
        if (result.Total > 0 && result.Items.Count == 0 && result.Page > result.TotalPages)
            result = await _uow.Repository<CategorieSalarie>().ToPagedAsync(
                predicate, order, result.TotalPages, result.PageSize, ct);

        var cards = await LoadCardsAsync(result.Items.Select(a => a.MatriculeSage).ToList(), ct);
        var matricules = cards.Values
            .Select(c => c.SageMatricule?.Trim())
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

    private async Task<List<AffectationRowDto>> LoadAffectationRowsAsync(
        int? categoryId, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var affectations = await _uow.Repository<CategorieSalarie>().ListAsync(
            a => a.BddSage == sage && (!categoryId.HasValue || a.IdCateg == categoryId), ct);

        var cards = await LoadCardsAsync(affectations.Select(a => a.MatriculeSage).ToList(), ct);
        var matricules = cards.Values
            .Select(c => c.SageMatricule?.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .ToList();
        var deptByMat = await LoadDepartementServicesAsync(matricules, ct);
        return affectations.Select(a => ToAffectationRow(a, cards, deptByMat)).ToList();
    }

    private async Task<Dictionary<int, CardPaie>> LoadCardsAsync(
        IReadOnlyCollection<int> cardIds, CancellationToken ct)
    {
        var result = new Dictionary<int, CardPaie>();
        foreach (var chunk in cardIds.Distinct().Chunk(500))
        {
            var ids = chunk.ToList();
            var cards = await _uow.Repository<CardPaie>().ListAsync(c => ids.Contains(c.Id), ct);
            foreach (var card in cards) result[card.Id] = card;
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
                var rows = await _departementServiceLookup.GetDepartementServiceBatchAsync(chunk, ct);
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
        CategorieSalarie affectation,
        IReadOnlyDictionary<int, CardPaie> cards,
        IReadOnlyDictionary<string, DepartementServiceDto> deptByMat)
    {
        cards.TryGetValue(affectation.MatriculeSage, out var card);
        var matricule = card?.SageMatricule?.Trim();
        deptByMat.TryGetValue(matricule ?? string.Empty, out var dept);
        return new AffectationRowDto(
            affectation.Id,
            affectation.MatriculeSage,
            affectation.IdCateg,
            matricule,
            card?.SageNom,
            card?.SagePrenom,
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

    public async Task<IReadOnlyList<AffectationDto>> ListAffectationsAsync(int? categoryId, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<CategorieSalarie>().ListReadOnlyAsync(
            a => a.BddSage == sage && (!categoryId.HasValue || a.IdCateg == categoryId), ct);
        return list.Select(a => new AffectationDto(a.Id, a.MatriculeSage, a.IdCateg)).ToList();
    }

    public async Task<AffectationDto> SaveAffectationAsync(AffectationDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        _ = await _uow.Repository<CardPaie>().GetByIdAsync(dto.CardPaieId, ct)
            ?? throw new NotFoundException($"Carte salarié introuvable (ID={dto.CardPaieId}).");
        _ = await _uow.Repository<Categorie>().GetByIdAsync(dto.CategoryId, ct)
            ?? throw new NotFoundException($"Catégorie introuvable (ID={dto.CategoryId}).");

        var entity = dto.Id == 0 ? await AffecterCarteAsync(dto, ct) : await ModifierAffectationAsync(dto, ct);
        await _uow.SaveChangesAsync(ct);
        return new AffectationDto(entity.Id, entity.MatriculeSage, entity.IdCateg);
    }

    /// <summary>Nouvelle affectation : une carte n'a qu'une catégorie, l'affectation existante est réutilisée.</summary>
    private async Task<CategorieSalarie> AffecterCarteAsync(AffectationDto dto, CancellationToken ct)
    {
        var repo = _uow.Repository<CategorieSalarie>();
        var existingForCard = (await repo.ListAsync(a => a.MatriculeSage == dto.CardPaieId, ct)).FirstOrDefault();
        if (existingForCard != null)
        {
            existingForCard.IdCateg = dto.CategoryId;
            repo.Update(existingForCard);
            return existingForCard;
        }
        var entity = new CategorieSalarie
        {
            MatriculeSage = dto.CardPaieId,
            IdCateg = dto.CategoryId,
            BddSage = _tenant.SageDb,
            BddPointeuse = _tenant.PointeuseDb
        };
        await repo.AddAsync(entity, ct);
        return entity;
    }

    private async Task<CategorieSalarie> ModifierAffectationAsync(AffectationDto dto, CancellationToken ct)
    {
        var repo = _uow.Repository<CategorieSalarie>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
        entity.MatriculeSage = dto.CardPaieId;
        entity.IdCateg = dto.CategoryId;
        repo.Update(entity);
        return entity;
    }

    public async Task DeleteAffectationAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<CategorieSalarie>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }
}
