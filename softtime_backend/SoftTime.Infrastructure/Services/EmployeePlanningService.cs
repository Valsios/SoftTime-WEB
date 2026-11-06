using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class EmployeePlanningService : IEmployeePlanningService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPlanningStatisticsReader _planningStats;
    private readonly IPlanningDatabaseFilter _planningDatabaseFilter;

    public EmployeePlanningService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IPlanningStatisticsReader planningStats,
        IPlanningDatabaseFilter planningDatabaseFilter)
    {
        _uow = uow;
        _tenant = tenant;
        _planningStats = planningStats;
        _planningDatabaseFilter = planningDatabaseFilter;
    }

    public async Task<IReadOnlyList<PlanningEmployeeDto>> ListEmployeesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var cards = await _uow.Repository<CardPaie>().ListReadOnlyAsync(_ => true, ct);
        return cards
            .Where(c => _planningDatabaseFilter.MatchesSage(c.SageBdd))
            .OrderBy(c => c.SageMatricule)
            .ThenBy(c => c.SageNom)
            .Select(c => new PlanningEmployeeDto(c.Id, c.SageMatricule, c.SageNom, c.SagePrenom))
            .ToList();
    }

    public async Task<IReadOnlyList<EmployeePlanningDto>> ListEmployeePlanningAsync(DateTime from, DateTime to, int? cardId, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var fromDate = from.Date;
        var toDate = to.Date;
        var cards = (await _uow.Repository<CardPaie>().ListReadOnlyAsync(_ => true, ct))
            .Where(c => _planningDatabaseFilter.MatchesSage(c.SageBdd))
            .ToDictionary(c => c.Id);
        var list = await _uow.Repository<PlanningSalarie>().ListReadOnlyAsync(
            p => (!cardId.HasValue || p.IdSal == cardId)
                 && p.DateP != null
                 && p.DateP >= fromDate.AddDays(-1)
                 && p.DateP <= toDate.AddDays(2), ct);
        return list
            .Where(p => p.DateP.HasValue
                        && p.DateP.Value.Date >= fromDate
                        && p.DateP.Value.Date <= toDate
                        && _planningDatabaseFilter.MatchesSage(p.BddSage)
                        && (!cardId.HasValue || p.IdSal == cardId.Value))
            .Select(p =>
            {
                cards.TryGetValue(p.IdSal, out var card);
                return new EmployeePlanningDto(
                    p.Id, p.IdSal, p.NoShift, p.DateP, p.Ha, p.Pause, p.Hd, p.OffP,
                    card?.SageMatricule, card?.SageNom, card?.SagePrenom);
            })
            .OrderBy(p => p.Matricule)
            .ThenBy(p => p.DateP)
            .ToList();
    }

    /// <summary>
    /// Liste paginée des lignes de planning. Le filtre date/carte est traduit en SQL ; les cartes
    /// (petite table) sont chargées une fois pour exposer matricule/nom/prénom.
    /// </summary>
    public async Task<PagedResult<EmployeePlanningDto>> ListEmployeePlanningPagedAsync(
        DateTime from, DateTime to, int? cardId, int? page, int? pageSize,
        string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var start = from.Date;
        var end = to.Date.AddDays(1);
        var cards = (await _uow.Repository<CardPaie>().ListReadOnlyAsync(EmployeeCardPredicate(sageDb), ct))
            .ToDictionary(c => c.Id);
        var hasSearch = SearchText.Terms(search).Count > 0;
        var cardIds = hasSearch
            ? cards.Values
                .Where(c => SearchText.Matches(search, c.SageMatricule, c.SageNom, c.SagePrenom))
                .Select(c => c.Id)
                .ToArray()
            : Array.Empty<int>();
        if (hasSearch && cardIds.Length == 0)
            return PagedLists.Page(Array.Empty<EmployeePlanningDto>(), page, pageSize);

        return await PagedLists.PageAsync(
            _uow.Repository<PlanningSalarie>(),
            p => p.DateP >= start
                 && p.DateP < end
                 && (p.BddSage == null || p.BddSage.Trim() == "" || p.BddSage.Trim() == sageDb)
                 && (!cardId.HasValue || p.IdSal == cardId.Value)
                 && (!hasSearch || cardIds.Contains(p.IdSal)),
            q => q.OrderBy(p => p.IdSal).ThenBy(p => p.DateP).ThenBy(p => p.Id),
            page,
            pageSize,
            p =>
            {
                cards.TryGetValue(p.IdSal, out var card);
                return new EmployeePlanningDto(
                    p.Id, p.IdSal, p.NoShift, p.DateP, p.Ha, p.Pause, p.Hd, p.OffP,
                    card?.SageMatricule, card?.SageNom, card?.SagePrenom);
            },
            ct);
    }

    /// <summary>Complet (export) : salariés + nombre de jours planifiés sur la période.</summary>
    public async Task<IReadOnlyList<PlanningEmployeeStatDto>> ListEmployeeStatsAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var counts = (await _planningStats.CountPlannedDaysAsync(sageDb, from, to, ct))
            .ToDictionary(x => x.CardPaieId, x => x.PlannedDays);
        var cards = await _uow.Repository<CardPaie>().ListReadOnlyAsync(EmployeeCardPredicate(sageDb), ct);
        return cards
            .Where(c => _planningDatabaseFilter.MatchesSage(c.SageBdd))
            .OrderBy(c => c.SageMatricule)
            .ThenBy(c => c.SageNom)
            .Select(c => new PlanningEmployeeStatDto(
                c.Id, c.SageMatricule, c.SageNom, c.SagePrenom, counts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    /// <summary>Grille paginée des salariés avec leur nombre de jours planifiés.</summary>
    public async Task<PagedResult<PlanningEmployeeStatDto>> ListEmployeeStatsPagedAsync(
        DateTime from, DateTime to, int? page, int? pageSize,
        string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var counts = (await _planningStats.CountPlannedDaysAsync(sageDb, from, to, ct))
            .ToDictionary(x => x.CardPaieId, x => x.PlannedDays);

        return await PagedLists.PageAsync(
            _uow.Repository<CardPaie>(),
            EmployeeCardPredicate(sageDb),
            q => q.OrderBy(c => c.SageMatricule).ThenBy(c => c.SageNom).ThenBy(c => c.Id),
            page,
            pageSize,
            c => new PlanningEmployeeStatDto(
                c.Id, c.SageMatricule, c.SageNom, c.SagePrenom, counts.GetValueOrDefault(c.Id)),
            search,
            new Expression<Func<CardPaie, string?>>[]
            {
                c => c.SageMatricule,
                c => c.SageNom,
                c => c.SagePrenom,
            },
            ct);
    }

    /// <summary>Filtre tenant des cartes : <c>SAGE_BDD</c> null/vide est accepté (lignes héritées).</summary>
    private static Expression<Func<CardPaie, bool>> EmployeeCardPredicate(string sageDb)
        => c => c.SageBdd == null || c.SageBdd.Trim() == "" || c.SageBdd.Trim() == sageDb;

    public async Task SaveEmployeePlanningAsync(IReadOnlyCollection<EmployeePlanningDto> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<PlanningSalarie>();
        var shifts = await _uow.Repository<Planning>().ListAsync(_ => true, ct);
        var cards = (await _uow.Repository<CardPaie>().ListAsync(_ => true, ct))
            .Where(c => _planningDatabaseFilter.MatchesSage(c.SageBdd) && _planningDatabaseFilter.MatchesPointeuse(c.PointeuseBdd))
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var dto in rows)
        {
            if (!dto.DateP.HasValue)
                throw new DomainException("La date du planning est requise.");
            if (!cards.ContainsKey(dto.CardPaieId))
                throw new DomainException($"Salarié introuvable pour la carte {dto.CardPaieId}.");

            var date = dto.DateP.Value.Date;
            var shift = dto.NoShift is int noShift
                ? shifts.FirstOrDefault(s => s.NoShift == noShift)
                : null;
            if (dto.NoShift is int shiftDemande && shift is null)
                throw new DomainException($"Shift {shiftDemande} introuvable.");

            PlanningSalarie? entity = null;
            if (dto.Id != 0)
            {
                entity = await repo.GetByIdAsync(dto.Id, ct)
                    ?? throw new DomainException($"Planning {dto.Id} introuvable.");
                if (!_planningDatabaseFilter.MatchesSage(entity.BddSage) || !_planningDatabaseFilter.MatchesPointeuse(entity.BddPointeuse))
                    throw new DomainException("Planning hors du périmètre de la base active.");
            }

            if (entity is null)
            {
                var existantes = await repo.ListAsync(
                    p => p.IdSal == dto.CardPaieId
                         && p.DateP != null
                         && p.DateP.Value.Date == date, ct);
                entity = existantes
                    .Where(p => _planningDatabaseFilter.MatchesSage(p.BddSage) && _planningDatabaseFilter.MatchesPointeuse(p.BddPointeuse))
                    .OrderBy(p => p.Id)
                    .FirstOrDefault();
            }

            var off = dto.Off == true;
            DateTime? ha = null;
            DateTime? hd = null;
            var pause = TimeSpan.Zero;

            if (!off)
            {
                // Une ligne héritée peut ne pas avoir de NoSHIFT : on conserve alors ses heures
                // existantes si l'écran n'en fournit pas.
                var debut = dto.Ha?.TimeOfDay ?? shift?.Ha ?? entity?.Ha?.TimeOfDay;
                var fin = dto.Hd?.TimeOfDay ?? shift?.Hd ?? entity?.Hd?.TimeOfDay;
                if (!PlanningGenerationRules.TryIntervalle(date, debut, fin, out ha, out hd))
                    throw new DomainException(
                        $"Heures d'entrée/sortie manquantes ou invalides pour le planning du {date:dd/MM/yyyy}.");
                pause = dto.Pause ?? shift?.Pause ?? entity?.Pause ?? TimeSpan.Zero;
            }

            if (entity == null)
            {
                await repo.AddAsync(new PlanningSalarie
                {
                    IdSal = dto.CardPaieId,
                    NoShift = dto.NoShift,
                    DateP = date,
                    Ha = ha,
                    Pause = pause,
                    Hd = hd,
                    OffP = off,
                    BddSage = _tenant.SageDb,
                    BddPointeuse = _tenant.PointeuseDb
                }, ct);
            }
            else
            {
                entity.IdSal = dto.CardPaieId;
                entity.NoShift = dto.NoShift;
                entity.DateP = date;
                entity.Ha = ha;
                entity.Pause = pause;
                entity.Hd = hd;
                entity.OffP = off;
                entity.BddSage = _tenant.SageDb;
                entity.BddPointeuse = _tenant.PointeuseDb;
                repo.Update(entity);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteEmployeePlanningAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<PlanningSalarie>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }
}
