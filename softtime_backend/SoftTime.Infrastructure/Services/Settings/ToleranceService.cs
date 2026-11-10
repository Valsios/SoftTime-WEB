using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Settings;

public sealed class ToleranceService : IToleranceService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ICategoryService _categoryService;

    public ToleranceService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ICategoryService categoryService)
    {
        _uow = uow;
        _tenant = tenant;
        _categoryService = categoryService;
    }

    public async Task<PagedResult<ToleranceDto>> ListTolerancesPagedAsync(
        int? categoryId, int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var labels = await _categoryService.LoadCategoryLabelsAsync(ct);
        var list = await _uow.Repository<ToleranceRetard>().ListReadOnlyAsync(
            t => !categoryId.HasValue || t.IdCategorie == categoryId, ct);
        var rows = list
            .OrderBy(t => t.IdCategorie)
            .ThenBy(t => t.Id)
            .Select(t => new ToleranceDto(t.Id, t.IdCategorie, t.Tolerance, t.TypesTol, t.Sortie))
            .Where(t => SearchText.Matches(
                search,
                labels.GetValueOrDefault(t.CategoryId),
                t.Tolerance,
                t.Sortie,
                t.TypesTol ? "oui" : "non"))
            .ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    public async Task<IReadOnlyList<ToleranceDto>> ListTolerancesAsync(int? categoryId, CancellationToken ct = default)
    {
        var list = await _uow.Repository<ToleranceRetard>().ListReadOnlyAsync(
            t => !categoryId.HasValue || t.IdCategorie == categoryId, ct);
        return list.Select(t => new ToleranceDto(t.Id, t.IdCategorie, t.Tolerance, t.TypesTol, t.Sortie)).ToList();
    }

    public async Task<ToleranceDto> SaveToleranceAsync(ToleranceDto dto, CancellationToken ct = default)
    {
        await ValiderAsync(dto, ct);

        var repo = _uow.Repository<ToleranceRetard>();
        ToleranceRetard entity;
        if (dto.Id == 0)
        {
            entity = new ToleranceRetard
            {
                IdCategorie = dto.CategoryId,
                Tolerance = dto.Tolerance,
                TypesTol = dto.TypesTol,
                Sortie = dto.Sortie
            };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            entity.IdCategorie = dto.CategoryId;
            entity.Tolerance = dto.Tolerance;
            entity.TypesTol = dto.TypesTol;
            entity.Sortie = dto.Sortie;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new ToleranceDto(entity.Id, entity.IdCategorie, entity.Tolerance, entity.TypesTol, entity.Sortie);
    }

    /// <summary>Catégorie existante et tolérances positives ou nulles.</summary>
    private async Task ValiderAsync(ToleranceDto dto, CancellationToken ct)
    {
        var categoryExists = await _uow.Repository<Categorie>().GetByIdAsync(dto.CategoryId, ct);
        if (categoryExists == null)
            throw new NotFoundException($"Catégorie introuvable (ID={dto.CategoryId}).");

        if (dto.Tolerance < 0)
            throw new DomainException("La tolérance d'entrée ne peut pas être négative.");

        if (dto.Sortie < 0)
            throw new DomainException("La tolérance de sortie ne peut pas être négative.");
    }

    public async Task DeleteToleranceAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<ToleranceRetard>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }
}
