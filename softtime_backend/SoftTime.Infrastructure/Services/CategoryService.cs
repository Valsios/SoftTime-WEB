using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ITenantConnectionService _tenant;

    public CategoryService(
        IUnitOfWork uow,
        IMapper mapper,
        ITenantConnectionService tenant)
    {
        _uow = uow;
        _mapper = mapper;
        _tenant = tenant;
    }

    public async Task<PagedResult<CategoryDto>> ListCategoriesPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var supervisorIds = SearchText.Terms(search).Count > 0
            ? await SupervisorIdsAsync(search, ct)
            : Array.Empty<int>();

        // Recherche sur l'intitulé OU sur le superviseur (matricule, nom, prénom).
        var searchPredicate = SearchExpression.For<Categorie>(search, c => c.Intitule);
        System.Linq.Expressions.Expression<Func<Categorie, bool>> predicate = c => c.BddSage == sage;
        if (supervisorIds.Length > 0)
        {
            System.Linq.Expressions.Expression<Func<Categorie, bool>> supervisorPredicate =
                c => supervisorIds.Contains(c.IdCardPaie);
            predicate = SearchExpression.AndAlso(
                predicate,
                searchPredicate is null ? supervisorPredicate : SearchExpression.OrElse(searchPredicate, supervisorPredicate));
        }
        else if (searchPredicate is not null)
        {
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        }

        return await PagedLists.From(_uow.Repository<Categorie>(), predicate, q => q.OrderBy(c => c.IdCategorie)).PageAsync(new PageRequest(page, pageSize), c => _mapper.Map<CategoryDto>(c), ct);
    }

    /// <summary>Cartes de la base active (ou sans base) dont le matricule, le nom ou le prénom correspond.</summary>
    private async Task<int[]> SupervisorIdsAsync(string? search, CancellationToken ct)
    {
        var sageKey = (_tenant.SageDb ?? string.Empty).Trim().ToLowerInvariant();
        var cards = await _uow.Repository<CardPaie>().ListReadOnlyAsync(
            c => c.SageBdd == null || c.SageBdd.Trim() == "" || c.SageBdd.Trim().ToLower() == sageKey,
            ct);
        return cards
            .Where(c => SearchText.Matches(search, c.SageMatricule, c.SageNom, c.SagePrenom))
            .Select(c => c.Id)
            .ToArray();
    }

    public async Task<Dictionary<int, string>> LoadCategoryLabelsAsync(CancellationToken ct)
    {
        var sage = _tenant.SageDb;
        var categories = await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == sage, ct);
        return categories.ToDictionary(
            category => category.IdCategorie,
            category => category.Intitule ?? "Catégorie introuvable");
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<Categorie>().ListReadOnlyAsync(c => c.BddSage == sage, ct);
        return list.Select(_mapper.Map<CategoryDto>).ToList();
    }

    public async Task<CategoryDto> SaveCategoryAsync(CategoryDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        _ = await _uow.Repository<CardPaie>().GetByIdAsync(dto.SupervisorCardId, ct)
            ?? throw new NotFoundException($"Carte superviseur introuvable (ID={dto.SupervisorCardId}).");
        if (string.IsNullOrWhiteSpace(dto.Intitule))
            throw new DomainException("L'intitulé de la catégorie est obligatoire.");
        if (dto.HeuresSemaine <= 0)
            throw new DomainException("Le nombre d'heures par semaine doit être supérieur à 0.");

        var repo = _uow.Repository<Categorie>();
        Categorie entity;
        if (dto.Id == 0)
        {
            entity = new Categorie { BddSage = _tenant.SageDb, BddPointeuse = _tenant.PointeuseDb };
            ApplyCategory(dto, entity);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            ApplyCategory(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CategoryDto>(entity);
    }

    private static void ApplyCategory(CategoryDto dto, Categorie entity)
    {
        entity.Intitule = dto.Intitule;
        entity.IdCardPaie = dto.SupervisorCardId;
        entity.Pause = dto.Pause;
        entity.HeureSemaine = dto.HeuresSemaine;
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Categorie>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }
}
