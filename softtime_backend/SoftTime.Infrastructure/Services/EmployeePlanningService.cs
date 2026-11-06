using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.PlanningImport;

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

        return await PagedLists.From(_uow.Repository<PlanningSalarie>(), p => p.DateP >= start
                 && p.DateP < end
                 && (p.BddSage == null || p.BddSage.Trim() == "" || p.BddSage.Trim() == sageDb)
                 && (!cardId.HasValue || p.IdSal == cardId.Value)
                 && (!hasSearch || cardIds.Contains(p.IdSal)), q => q.OrderBy(p => p.IdSal).ThenBy(p => p.DateP).ThenBy(p => p.Id)).PageAsync(new PageRequest(page, pageSize), p =>
            {
                cards.TryGetValue(p.IdSal, out var card);
                return new EmployeePlanningDto(
                    p.Id, p.IdSal, p.NoShift, p.DateP, p.Ha, p.Pause, p.Hd, p.OffP,
                    card?.SageMatricule, card?.SageNom, card?.SagePrenom);
            }, ct);
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

        return await PagedLists.From(_uow.Repository<CardPaie>(), EmployeeCardPredicate(sageDb), q => q.OrderBy(c => c.SageMatricule).ThenBy(c => c.SageNom).ThenBy(c => c.Id)).WithSearch(search, new Expression<Func<CardPaie, string?>>[]
            {
                c => c.SageMatricule,
                c => c.SageNom,
                c => c.SagePrenom,
            }).PageAsync(new PageRequest(page, pageSize), c => new PlanningEmployeeStatDto(
                c.Id, c.SageMatricule, c.SageNom, c.SagePrenom, counts.GetValueOrDefault(c.Id)), ct);
    }

    /// <summary>Filtre tenant des cartes : <c>SAGE_BDD</c> null/vide est accepté (lignes héritées).</summary>
    private static Expression<Func<CardPaie, bool>> EmployeeCardPredicate(string sageDb)
        => c => c.SageBdd == null || c.SageBdd.Trim() == "" || c.SageBdd.Trim() == sageDb;

    public async Task SaveEmployeePlanningAsync(IReadOnlyCollection<EmployeePlanningDto> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var shifts = await _uow.Repository<Planning>().ListAsync(_ => true, ct);
        var cards = (await _uow.Repository<CardPaie>().ListAsync(_ => true, ct))
            .Where(c => _planningDatabaseFilter.MatchesSage(c.SageBdd) && _planningDatabaseFilter.MatchesPointeuse(c.PointeuseBdd))
            .Select(c => c.Id)
            .ToHashSet();

        foreach (var dto in rows)
            await SaveRowAsync(dto, shifts, cards, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task SaveRowAsync(EmployeePlanningDto dto, List<Planning> shifts, HashSet<int> cards, CancellationToken ct)
    {
        if (!dto.DateP.HasValue)
            throw new DomainException("La date du planning est requise.");
        if (!cards.Contains(dto.CardPaieId))
            throw new DomainException($"Salarié introuvable pour la carte {dto.CardPaieId}.");

        var date = dto.DateP.Value.Date;
        var shift = dto.NoShift is int noShift ? shifts.FirstOrDefault(s => s.NoShift == noShift) : null;
        if (dto.NoShift is int shiftDemande && shift is null)
            throw new DomainException($"Shift {shiftDemande} introuvable.");

        var repo = _uow.Repository<PlanningSalarie>();
        var entity = await FindPlanningAsync(dto, date, ct);
        var valeurs = Valeurs(dto, date, shift, entity);
        if (entity == null)
        {
            var planning = new PlanningSalarie { IdSal = dto.CardPaieId, DateP = date };
            valeurs.Appliquer(planning, _tenant.SageDb, _tenant.PointeuseDb);
            await repo.AddAsync(planning, ct);
            return;
        }
        entity.IdSal = dto.CardPaieId;
        entity.DateP = date;
        valeurs.Appliquer(entity, _tenant.SageDb, _tenant.PointeuseDb);
        repo.Update(entity);
    }

    /// <summary>Ligne désignée par son identifiant (dans le périmètre actif), sinon celle du salarié ce jour-là.</summary>
    private async Task<PlanningSalarie?> FindPlanningAsync(EmployeePlanningDto dto, DateTime date, CancellationToken ct)
    {
        var repo = _uow.Repository<PlanningSalarie>();
        if (dto.Id != 0)
        {
            var entity = await repo.GetByIdAsync(dto.Id, ct)
                ?? throw new DomainException($"Planning {dto.Id} introuvable.");
            if (!_planningDatabaseFilter.MatchesSage(entity.BddSage) || !_planningDatabaseFilter.MatchesPointeuse(entity.BddPointeuse))
                throw new DomainException("Planning hors du périmètre de la base active.");
            return entity;
        }

        var existantes = await repo.ListAsync(
            p => p.IdSal == dto.CardPaieId && p.DateP != null && p.DateP.Value.Date == date, ct);
        return existantes
            .Where(p => _planningDatabaseFilter.MatchesSage(p.BddSage) && _planningDatabaseFilter.MatchesPointeuse(p.BddPointeuse))
            .OrderBy(p => p.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Heures saisies, sinon celles du shift, sinon celles de la ligne existante : une ligne héritée
    /// peut ne pas avoir de NoSHIFT et conserve alors ses heures si l'écran n'en fournit pas.
    /// </summary>
    private static PlanningDayValues Valeurs(EmployeePlanningDto dto, DateTime date, Planning? shift, PlanningSalarie? entity)
    {
        if (dto.Off == true)
            return PlanningDayValues.Repos with { NoShift = dto.NoShift };

        var debut = dto.Ha?.TimeOfDay ?? shift?.Ha ?? entity?.Ha?.TimeOfDay;
        var fin = dto.Hd?.TimeOfDay ?? shift?.Hd ?? entity?.Hd?.TimeOfDay;
        if (!PlanningGenerationRules.TryIntervalle(date, debut, fin, out var ha, out var hd))
            throw new DomainException(
                $"Heures d'entrée/sortie manquantes ou invalides pour le planning du {date:dd/MM/yyyy}.");
        var pause = dto.Pause ?? shift?.Pause ?? entity?.Pause ?? TimeSpan.Zero;
        return new PlanningDayValues(dto.NoShift, ha, hd, pause, false);
    }

    public async Task DeleteEmployeePlanningAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<PlanningSalarie>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }
}
