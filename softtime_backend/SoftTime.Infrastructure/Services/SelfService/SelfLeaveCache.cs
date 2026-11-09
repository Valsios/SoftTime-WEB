using System.Collections.Concurrent;
using System.Globalization;

using SoftTime.Application.Abstractions.SelfService;

namespace SoftTime.Infrastructure.Services.SelfService;

/// <summary>
/// Congés SAGE de « Mon espace ». La source SAGE n'offre aucune lecture filtrée : lire les congés coûte plusieurs
/// secondes (tout le tenant, mesuré 2,7 à 5 s, jusqu'à 13 s). Deux niveaux :
/// <list type="number">
///   <item><b>Chargement partagé par base</b> : une seule lecture du tenant à la fois (« en vol »), quel que soit
///   le nombre de salariés qui la demandent ; les suivants attendent la même lecture. Le résultat est conservé
///   <see cref="Duree"/> en mémoire, à l'INTÉRIEUR de cette classe : aucun membre public ne le rend, et chaque
///   lecture n'extrait que la clé du demandeur.</item>
///   <item><b>Entrée par salarié</b> (clé utilisateur | base | matricule) : la part du salarié, copiée, jamais
///   servie à un autre utilisateur ni à un autre matricule ; elle n'est jamais plus vieille que le chargement dont
///   elle vient.</item>
/// </list>
/// Un échec n'est jamais conservé (la lecture suivante réessaie). Plafond mémoire : au plus <see cref="MaxBases"/>
/// chargements de tenant et <see cref="MaxEntrees"/> parts de salarié. Enregistré en singleton.
/// </summary>
public sealed class SelfLeaveCache : ISelfLeaveCache
{
    public const int MaxEntrees = 512;
    public const int MaxBases = 8;

    public static readonly TimeSpan Duree = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Part> _entrees = new();
    private readonly ConcurrentDictionary<string, Chargement> _tenants = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<Dictionary<string, List<LeaveDay>>>>> _vols = new();
    private readonly TimeProvider _horloge;
    private int _lectures;

    public SelfLeaveCache(TimeProvider horloge)
    {
        _horloge = horloge;
    }

    /// <summary>Nombre de lectures du tenant effectivement lancées (mesure et tests).</summary>
    public int LecturesTenant => Volatile.Read(ref _lectures);

    public static string Cle(SelfContext me)
        => string.Join('|', ((long)me.UserId).ToString(CultureInfo.InvariantCulture), CleBase(me.SageDb), me.Cle);

    /// <summary>
    /// <paramref name="chargerTenant"/> est appelée sans jeton d'annulation d'un appelant particulier : l'appelant qui
    /// la déclenche attend sa fin, les autres attendent la même lecture (et peuvent, eux, être annulés via <paramref
    /// name="ct"/>).
    /// </summary>
    public async Task<List<LeaveDay>> ObtenirAsync(
        SelfContext me, Func<Task<Dictionary<string, List<LeaveDay>>>> chargerTenant, CancellationToken ct = default)
    {
        var cle = Cle(me);
        if (_entrees.TryGetValue(cle, out var e) && e.Expire > _horloge.GetUtcNow())
            return e.Valeur;

        var tenant = await TenantAsync(CleBase(me.SageDb), chargerTenant, ct);
        var siens = tenant.ParCle.TryGetValue(me.Cle, out var l) ? new List<LeaveDay>(l) : new List<LeaveDay>();
        Conserver(cle, siens, tenant.Expire);
        return siens;
    }

    private static string CleBase(string sageDb) => sageDb.Trim().ToUpperInvariant();

    private void Conserver(string cle, List<LeaveDay> siens, DateTimeOffset expireTenant)
    {
        var maintenant = _horloge.GetUtcNow();
        foreach (var (k, v) in _entrees)
            if (v.Expire <= maintenant)
                _entrees.TryRemove(k, out _);
        while (_entrees.Count >= MaxEntrees && _entrees.Keys.FirstOrDefault() is string ancienne)
            _entrees.TryRemove(ancienne, out _);
        var expire = maintenant + Duree;
        _entrees[cle] = new Part(expire < expireTenant ? expire : expireTenant, siens);
    }

    private async Task<Chargement> TenantAsync(
        string cleBase, Func<Task<Dictionary<string, List<LeaveDay>>>> charger, CancellationToken ct)
    {
        if (_tenants.TryGetValue(cleBase, out var c) && c.Expire > _horloge.GetUtcNow())
            return c;

        var nouveau = new Lazy<Task<Dictionary<string, List<LeaveDay>>>>(() =>
        {
            Interlocked.Increment(ref _lectures);
            return charger();
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        var vol = _vols.GetOrAdd(cleBase, nouveau);

        if (!ReferenceEquals(vol, nouveau))
        {
            // Une lecture est déjà en vol pour cette base : on attend la même (seule l'attente est annulable).
            var tout = await vol.Value.WaitAsync(ct);
            return _tenants.TryGetValue(cleBase, out c) && c.ParCle == tout
                ? c
                : new Chargement(_horloge.GetUtcNow() + Duree, tout);
        }

        try
        {
            return await TerminerLectureAsync(cleBase, vol);
        }
        finally
        {
            _vols.TryRemove(new KeyValuePair<string, Lazy<Task<Dictionary<string, List<LeaveDay>>>>>(cleBase, vol));
        }
    }

    /// <summary>
    /// Lecture lancée par cet appelant : il en attend la fin sans jeton d'annulation, car sa portée (requête) reste
    /// vivante tant que d'autres salariés en dépendent.
    /// </summary>
    private async Task<Chargement> TerminerLectureAsync(
        string cleBase, Lazy<Task<Dictionary<string, List<LeaveDay>>>> vol)
    {
        // Une lecture vient de se terminer entre le premier contrôle et l'inscription : inutile d'en lancer une autre.
        if (_tenants.TryGetValue(cleBase, out var c) && c.Expire > _horloge.GetUtcNow())
            return c;

        var charge = new Chargement(_horloge.GetUtcNow() + Duree, await vol.Value);
        var maintenant = _horloge.GetUtcNow();
        foreach (var (k, v) in _tenants)
            if (v.Expire <= maintenant)
                _tenants.TryRemove(k, out _);
        while (_tenants.Count >= MaxBases
               && _tenants.OrderBy(t => t.Value.Expire).Select(t => t.Key).FirstOrDefault() is string vieille)
            _tenants.TryRemove(vieille, out _);
        _tenants[cleBase] = charge;
        return charge;
    }

    private sealed record Part(DateTimeOffset Expire, List<LeaveDay> Valeur);

    private sealed record Chargement(DateTimeOffset Expire, Dictionary<string, List<LeaveDay>> ParCle);
}
