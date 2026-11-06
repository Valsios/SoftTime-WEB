using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class CardPaieService : ICardPaieService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ITenantConnectionService _tenant;
    private readonly ICorrespondenceModeService _correspondenceModeService;

    public CardPaieService(
        IUnitOfWork uow,
        IMapper mapper,
        ITenantConnectionService tenant,
        ICorrespondenceModeService correspondenceModeService)
    {
        _uow = uow;
        _mapper = mapper;
        _tenant = tenant;
        _correspondenceModeService = correspondenceModeService;
    }

    public async Task<PagedResult<CardPaieDto>> ListCardsPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = (_tenant.SageDb ?? string.Empty).Trim().ToLowerInvariant();
        System.Linq.Expressions.Expression<Func<CardPaie, bool>> predicate = c =>
            c.SageBdd == null
            || c.SageBdd.Trim() == ""
            || c.SageBdd.Trim().ToLower() == sage;

        return await PagedLists.PageAsync(
            _uow.Repository<CardPaie>(),
            predicate,
            q => q.OrderBy(c => c.Id),
            page,
            pageSize,
            c => _mapper.Map<CardPaieDto>(c),
            search,
            new System.Linq.Expressions.Expression<Func<CardPaie, string?>>[]
            {
                c => c.SageMatricule,
                c => c.SageNom,
                c => c.SagePrenom,
                c => c.PointeuseNumero,
                c => c.PointeuseNom,
                c => c.Departement,
            },
            ct);
    }

    public async Task<IReadOnlyList<CardPaieDto>> ListCardsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = (await _uow.Repository<CardPaie>().ListReadOnlyAsync(_ => true, ct))
            .Where(c =>
            {
                if (string.IsNullOrWhiteSpace(c.SageBdd)) return true;
                return string.Equals(c.SageBdd.Trim(), sage.Trim(), StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        return list.Select(_mapper.Map<CardPaieDto>).ToList();
    }

    public async Task<CardPaieDto> SaveCardAsync(CardPaieDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<CardPaie>();
        CardPaie entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<CardPaie>(dto);
            entity.SageBdd ??= _tenant.SageDb;
            entity.Date ??= DateTime.Now;
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            _mapper.Map(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CardPaieDto>(entity);
    }

    public async Task DeleteCardAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<CardPaie>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
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
            throw new DomainException("Matricule SAGE requis.");
        var sage = _tenant.SageDb;
        var repo = _uow.Repository<CardPaie>();
        var candidates = (await repo.ListAsync(c =>
                c.SageBdd == sage && c.SageMatricule == matriculeSage, ct))
            .Where(c => MatriculeIdentity.MemeSalarie(c.SageMatricule, matriculeSage))
            .Where(c => string.IsNullOrWhiteSpace(matriculePointeuse)
                        || MatriculeIdentity.MemeSalarie(c.PointeuseNumero, matriculePointeuse))
            .ToList();
        if (candidates.Count == 0)
            throw new NotFoundException($"Aucune correspondance {matriculeSage} ↔ {matriculePointeuse ?? "*"} sur la base {sage}.");
        repo.RemoveRange(candidates);
        await _uow.SaveChangesAsync(ct);
        return candidates.Count;
    }

    public async Task<SyncResultDto> AutoMapCardsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var mode = await _correspondenceModeService.GetCorrespondenceAsync(ct);
        if (mode.Active)
            throw new DomainException(
                "Le mode correspondance manuelle est actif pour cette paire de bases : la synchronisation automatique est désactivée. Désactivez le mode pour la relancer.");
        return await _correspondenceModeService.SyncCardPaieCoreAsync(sageRow, pteRow, ct);
    }

    public async Task<ImportResultDto> ImportCorrespondencesCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var existing = await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == _tenant.SageDb, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var parts in lines)
        {
            var ligne = LireCorrespondance(parts);
            if (ligne is null)
                continue;
            if (ligne.Value.Matricule.Length == 0)
            {
                skipped++;
                continue;
            }
            await EnregistrerCorrespondanceAsync(existing, ligne.Value.Badge, ligne.Value.Matricule, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped,
            $"{imported} correspondance(s) importée(s), {skipped} ligne(s) ignorée(s).");
    }

    /// <summary>
    /// Ligne « badge ; matricule » : <c>null</c> pour un en-tête (ignoré sans être compté), matricule
    /// vide pour une ligne inexploitable (comptée comme ignorée).
    /// </summary>
    private static (string Badge, string Matricule)? LireCorrespondance(string[] parts)
    {
        if (parts.Length < 2)
            return (string.Empty, string.Empty);
        var badge = parts[0].Trim();
        var matricule = parts[1].Trim();
        if (ExcelCells.Fold(badge) is "BADGE" or "POINTEUSE" or "NPOINTEUSE" or "NUMERO")
            return null;
        if (string.IsNullOrWhiteSpace(matricule))
            return (badge, string.Empty);
        // En-tête décalé (colonne matricule) : même traitement que l'en-tête.
        return ExcelCells.Fold(matricule) is "MATRICULE" or "MATRICULESAGE" ? null : (badge, matricule);
    }

    private async Task EnregistrerCorrespondanceAsync(List<CardPaie> existing, string badge, string matricule, CancellationToken ct)
    {
        var repo = _uow.Repository<CardPaie>();
        var card = existing.FirstOrDefault(c => string.Equals(c.SageMatricule, matricule, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.PointeuseNumero = badge;
            repo.Update(card);
            return;
        }
        card = new CardPaie
        {
            SageMatricule = matricule,
            PointeuseNumero = badge,
            SageBdd = _tenant.SageDb,
            PointeuseBdd = _tenant.PointeuseDb,
            Date = DateTime.Now
        };
        await repo.AddAsync(card, ct);
        existing.Add(card);
    }
}
