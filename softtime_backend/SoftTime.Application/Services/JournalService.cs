using System.Linq.Expressions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Lecture seule du journal central <c>suivi_JOURNAL</c> (base de l'application, indépendante de la base SAGE choisie).
/// Accès : administrateur, ou porte 5 ET permission <c>TRACE:AUDIT</c> effectives. Les valeurs sensibles sont
/// masquées à la lecture par <see cref="AuditMaskingPolicy"/>, même si une ancienne ligne a été stockée en clair.
/// Ce service n'écrit jamais : aucune modification ni suppression du journal n'est exposée.
/// </summary>
public class JournalService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxFacetValues = 200;

    /// <summary>Actions écrites par l'intercepteur EF (Insertion, Modification, Suppression) et par la synchro paie.</summary>
    public static readonly IReadOnlyList<string> KnownActions = ["Insertion", "Modification", "Suppression", "SYNC_PAIE"];

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly EffectiveRightsService _rights;

    public JournalService(IUnitOfWork uow, ICurrentUser user, EffectiveRightsService rights)
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
        var repo = _uow.Repository<suivi_JOURNAL>();
        var filtered = repo.Query().Where(Filter(query));

        var total = await repo.CountAsync(filtered, ct);
        var offset = (long)(page - 1) * pageSize;
        var rows = offset >= total
            ? new List<suivi_JOURNAL>()
            : await repo.ToListAsync(filtered
                .OrderByDescending(e => e.DATE_UTC)
                .ThenByDescending(e => e.Id)
                .Skip((int)offset)
                .Take(pageSize), ct);

        return new JournalPageDto(rows.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<JournalEntryDto> GetAsync(long id, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);

        var repo = _uow.Repository<suivi_JOURNAL>();
        var row = (await repo.ToListAsync(repo.Query().Where(e => e.Id == id).Take(1), ct)).FirstOrDefault()
                  ?? throw new NotFoundException("Ligne de journal introuvable.");
        return ToDto(row);
    }

    public async Task<JournalFacetsDto> GetFacetsAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);

        var repo = _uow.Repository<suivi_JOURNAL>();
        var period = repo.Query().Where(Filter(new JournalQuery(From: from, To: to)));
        var tables = await repo.ToListAsync(period.Select(e => e.TABLES).Distinct().OrderBy(v => v).Take(MaxFacetValues), ct);
        var actions = await repo.ToListAsync(period.Select(e => e.ACTIONS).Distinct().OrderBy(v => v).Take(MaxFacetValues), ct);
        var users = await repo.ToListAsync(period.Select(e => e.USERS).Distinct().OrderBy(v => v).Take(MaxFacetValues), ct);
        return new JournalFacetsDto(tables, actions, users);
    }

    /// <summary>Lecture du journal : administrateur, ou porte 5 ET permission TRACE:AUDIT effectives.</summary>
    private Task EnsureCanReadAsync(CancellationToken ct)
        => FineReadAccess.EnsureAsync(_user, _rights, Rights.Traceability, "TRACE:AUDIT", ct);

    /// <summary>Filtres traduits en paramètres SQL par EF (aucune valeur concaténée dans une requête).</summary>
    private static Expression<Func<suivi_JOURNAL, bool>> Filter(JournalQuery q)
    {
        var from = Utc(q.From);
        var to = Utc(q.To);
        var user = Blank(q.User);
        var action = Blank(q.Action);
        var table = Blank(q.Table);
        var key = Blank(q.Key);
        var correlation = q.Correlation;
        var sage = Blank(q.SageDatabase);
        return e => (from == null || e.DATE_UTC >= from)
                    && (to == null || e.DATE_UTC <= to)
                    && (user == null || e.USERS == user)
                    && (action == null || e.ACTIONS == action)
                    && (table == null || e.TABLES == table)
                    && (key == null || e.CLES == key)
                    && (correlation == null || e.CORRELATIONS == correlation)
                    && (sage == null || e.BASE_SAGE == sage);
    }

    /// <summary>
    /// DATE_UTC est stockée en UTC. Une date reçue avec un décalage (« Z », « +04:00 ») est liée en heure locale par
    /// ASP.NET Core : on la ramène en UTC ; une date sans indication est prise telle quelle, comme une heure UTC.
    /// </summary>
    private static DateTime? Utc(DateTime? value)
        => value?.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JournalEntryDto ToDto(suivi_JOURNAL e) => new(
        e.Id,
        e.DATE_UTC,
        e.TABLES,
        AuditMaskingPolicy.MaskKey(e.TABLES, e.CLES),
        e.COLONNES,
        e.ACTIONS,
        AuditMaskingPolicy.MaskValue(e.TABLES, e.COLONNES, e.ANCIENS),
        AuditMaskingPolicy.MaskValue(e.TABLES, e.COLONNES, e.NOUVEAUX),
        e.USERS,
        e.BASE_SAGE,
        e.BASE_POINTEUSE,
        e.CORRELATIONS,
        e.SOURCE);
}
