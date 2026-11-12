using System.Linq.Expressions;

using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Audit;

/// <summary>
/// Lecture seule du journal central <c>suivi_JOURNAL</c> (base de l'application, indépendante de la base SAGE choisie).
/// Accès : administrateur, ou porte 5 ET permission <c>audit.read</c> effectives. Les valeurs sensibles sont
/// masquées à la lecture par <see cref="AuditMaskingPolicy"/>, même si une ancienne ligne a été stockée en clair.
/// Ce service n'écrit jamais : aucune modification ni suppression du journal n'est exposée.
/// </summary>
public class JournalService : IJournalService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxFacetValues = 200;

    /// <summary>Actions écrites par l'intercepteur EF (Insertion, Modification, Suppression) et par la synchro paie.</summary>
    public static readonly IReadOnlyList<string> KnownActions = ["Insertion", "Modification", "Suppression", "SYNC_PAIE"];

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IEffectiveRightsService _rights;

    public JournalService(IUnitOfWork uow, ICurrentUser user, IEffectiveRightsService rights)
    {
        _uow = uow;
        _user = user;
        _rights = rights;
    }

    public async Task<JournalPageDto> ListAsync(JournalQuery query, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);

        var page = query.Page is null or < 1 ? 1 : query.Page.Value;
        var pageSize = query.PageSize is null or < 1 ? DefaultPageSize : Math.Min(query.PageSize.Value, MaxPageSize);
        var repo = _uow.Repository<JournalAudit>();
        var filtered = repo.QueryReadOnly().Where(Filter(query));

        var total = await repo.CountAsync(filtered, ct);
        var offset = (long)(page - 1) * pageSize;
        var rows = offset >= total
            ? new List<JournalAudit>()
            : await repo.ToListAsync(filtered
                .OrderByDescending(e => e.DateUtc)
                .ThenByDescending(e => e.Id)
                .Skip((int)offset)
                .Take(pageSize), ct);

        return new JournalPageDto(rows.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<JournalEntryDto> GetAsync(long id, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);

        var repo = _uow.Repository<JournalAudit>();
        var row = (await repo.ToListAsync(repo.QueryReadOnly().Where(e => e.Id == id).Take(1), ct)).FirstOrDefault()
                  ?? throw new NotFoundException("Ligne de journal introuvable.");
        return ToDto(row);
    }

    public async Task<JournalFacetsDto> GetFacetsAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);

        var repo = _uow.Repository<JournalAudit>();
        var period = repo.QueryReadOnly().Where(Filter(new JournalQuery(From: from, To: to)));
        var tables = await repo.ToListAsync(period.Select(e => e.Tables).Distinct().OrderBy(v => v).Take(MaxFacetValues), ct);
        var actions = await repo.ToListAsync(period.Select(e => e.Actions).Distinct().OrderBy(v => v).Take(MaxFacetValues), ct);
        var users = await repo.ToListAsync(period.Select(e => e.Users).Distinct().OrderBy(v => v).Take(MaxFacetValues), ct);
        return new JournalFacetsDto(tables, actions, users);
    }

    /// <summary>Lecture du journal : administrateur, ou porte 5 ET permission audit.read effectives.</summary>
    private Task EnsureCanReadAsync(CancellationToken ct)
        => FineReadAccess.EnsureAsync(_user, _rights, Rights.Traceability, "audit.read", ct);

    /// <summary>Filtres traduits en paramètres SQL par EF (aucune valeur concaténée dans une requête).</summary>
    private static Expression<Func<JournalAudit, bool>> Filter(JournalQuery q)
    {
        var from = Utc(q.From);
        var to = Utc(q.To);
        var user = Blank(q.User);
        var action = Blank(q.Action);
        var table = Blank(q.Table);
        var key = Blank(q.Key);
        var correlation = q.Correlation;
        var sage = Blank(q.SageDatabase);
        return e => (from == null || e.DateUtc >= from)
                    && (to == null || e.DateUtc <= to)
                    && (user == null || e.Users == user)
                    && (action == null || e.Actions == action)
                    && (table == null || e.Tables == table)
                    && (key == null || e.Cles == key)
                    && (correlation == null || e.Correlations == correlation)
                    && (sage == null || e.BaseSage == sage);
    }

    /// <summary>
    /// DATE_UTC est stockée en UTC. Une date reçue avec un décalage (« Z », « +04:00 ») est liée en heure locale par
    /// ASP.NET Core : on la ramène en UTC ; une date sans indication est prise telle quelle, comme une heure UTC.
    /// </summary>
    private static DateTime? Utc(DateTime? value)
        => value?.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JournalEntryDto ToDto(JournalAudit e) => new(
        e.Id,
        e.DateUtc,
        e.Tables,
        AuditMaskingPolicy.MaskKey(e.Tables, e.Cles),
        e.Colonnes,
        e.Actions,
        AuditMaskingPolicy.MaskValue(e.Tables, e.Colonnes, e.Anciens),
        AuditMaskingPolicy.MaskValue(e.Tables, e.Colonnes, e.Nouveaux),
        e.Users,
        e.BaseSage,
        e.BasePointeuse,
        e.Correlations,
        e.Source);
}
