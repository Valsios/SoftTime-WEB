using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly IUnitOfWork _uow;

    public AuditService(IUnitOfWork uow) => _uow = uow;

    public IReadOnlyList<AuditTypeOption> ListAllowedTypes() => AuditConfigurationRules.AllowedTypeOptions;

    public async Task<IReadOnlyList<AuditConfigDto>> ListConfigAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<SuiviConfiguration>().ListReadOnlyAsync(_ => true, ct);
        return list.Select(c => new AuditConfigDto(c.Id, c.Tables, c.Colonnes, c.Types, c.Etats, c.Bases)).ToList();
    }

    public async Task SaveConfigAsync(AuditConfigDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<SuiviConfiguration>();
        if (dto.Id == 0)
        {
            AuditConfigurationRules.Valider(dto.Type);
            var typeCanonique = AuditConfigurationRules.Normalize(dto.Type!);

            await repo.AddAsync(new SuiviConfiguration
            {
                Tables = dto.Table,
                Colonnes = dto.Column,
                Types = typeCanonique,
                Etats = dto.Enabled,
                Bases = dto.Base
            }, ct);
        }
        else
        {
            await ModifierAsync(dto, repo, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Met à jour une règle de suivi ; un type inchangé (sans tenir compte de la casse) est conservé tel quel.</summary>
    private static async Task ModifierAsync(
        AuditConfigDto dto, IRepository<SuiviConfiguration> repo, CancellationToken ct)
    {
        var e = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
        e.Tables = dto.Table;
        e.Colonnes = dto.Column;

        // Si le type a été modifié par rapport à l'existant (comparaison insensible)
        var typeInchange = string.Equals(e.Types, dto.Type, StringComparison.OrdinalIgnoreCase);
        if (!typeInchange)
        {
            AuditConfigurationRules.Valider(dto.Type);
            e.Types = AuditConfigurationRules.Normalize(dto.Type!);
        }
        // Si le type est inchangé, on préserve la valeur existante en base sans la réécrire ni la normaliser

        e.Etats = dto.Enabled;
        e.Bases = dto.Base;
        repo.Update(e);
    }

    public async Task<IReadOnlyList<AuditEventDto>> ListEventsAsync(DateTime? from, DateTime? to, string? table, string? action, CancellationToken ct = default)
    {
        var fromDate = from ?? DateTime.Today.AddDays(-30);
        var toDate = to ?? DateTime.Today.AddDays(1);
        var list = await _uow.Repository<SuiviModification>().ListLatestAsync(
            e => e.Datetimes >= fromDate
                 && e.Datetimes <= toDate
                 && (string.IsNullOrEmpty(table) || e.Tables == table)
                 && (string.IsNullOrEmpty(action) || e.Actions == action),
            e => e.Datetimes,
            take: 500,
            ct);
        return list
            .Select(e => new AuditEventDto(e.Id, e.Tables, e.Colonnes, e.Users, e.Actions, e.Datetimes, e.Nouveaux, e.Anciens, e.Bases))
            .ToList();
    }

    /// <summary>Configuration d'audit paginée (petite table, mais contrat identique aux autres grilles).</summary>
    public async Task<PagedResult<AuditConfigDto>> ListConfigPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        var searchPredicate = SearchExpression.For<SuiviConfiguration>(
            search, c => c.Tables, c => c.Colonnes, c => c.Bases);
        System.Linq.Expressions.Expression<Func<SuiviConfiguration, bool>> predicate = _ => true;
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        return await PagedLists.From(_uow.Repository<SuiviConfiguration>(), predicate, q => q.OrderBy(c => c.Id)).PageAsync(new PageRequest(page, pageSize), c => new AuditConfigDto(c.Id, c.Tables, c.Colonnes, c.Types, c.Etats, c.Bases), ct);
    }

    /// <summary>
    /// Journal d'audit paginé. Contrairement à <see cref="ListEventsAsync"/> (plafond 500 pour
    /// l'export), le total et la page sont calculés en SQL sur le filtre complet.
    /// </summary>
    public async Task<PagedResult<AuditEventDto>> ListEventsPagedAsync(
        DateTime? from, DateTime? to, string? table, string? action, string? search,
        int? page, int? pageSize, CancellationToken ct = default)
    {
        var fromDate = from ?? DateTime.Today.AddDays(-30);
        var toDate = to ?? DateTime.Today.AddDays(1);
        var tableFiltre = string.IsNullOrWhiteSpace(table) ? null : table;
        var actionFiltre = string.IsNullOrWhiteSpace(action) ? null : action;
        var searchPredicate = SearchExpression.For<SuiviModification>(
            search,
            e => e.Tables,
            e => e.Colonnes,
            e => e.Users,
            e => e.Actions,
            e => e.Nouveaux,
            e => e.Anciens,
            e => e.Bases);
        System.Linq.Expressions.Expression<Func<SuiviModification, bool>> predicate =
            e => e.Datetimes >= fromDate
                 && e.Datetimes <= toDate
                 && (tableFiltre == null || e.Tables == tableFiltre)
                 && (actionFiltre == null || e.Actions == actionFiltre);
        if (searchPredicate is not null)
            predicate = SearchExpression.AndAlso(predicate, searchPredicate);
        return await PagedLists.From(_uow.Repository<SuiviModification>(), predicate, q => q.OrderByDescending(e => e.Datetimes).ThenByDescending(e => e.Id)).PageAsync(new PageRequest(page, pageSize), e => new AuditEventDto(e.Id, e.Tables, e.Colonnes, e.Users, e.Actions, e.Datetimes, e.Nouveaux, e.Anciens, e.Bases), ct);
    }

    public async Task<IReadOnlyList<object>> ListDiscoveredSchemaAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<SuiviTable>().ListReadOnlyAsync(_ => true, ct);
        return list.Select(t => (object)new { t.Id, S_TABLES = t.STables, S_COLONNES = t.SColonnes, S_TYPES = t.STypes, BASES = t.Bases }).ToList();
    }
}
