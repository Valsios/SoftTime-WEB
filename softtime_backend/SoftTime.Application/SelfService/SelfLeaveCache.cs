using System.Collections.Concurrent;
using SoftTime.Domain.Services;

namespace SoftTime.Application.SelfService;

/// <summary>
/// Congés SAGE de « Mon espace ». La source SAGE n'offre aucune lecture filtrée : lire les congés coûte plusieurs secondes
/// (tout le tenant, mesuré 2,7 à 5 s, jusqu'à 13 s). Deux niveaux :
/// <list type="number">
///   <item><b>Chargement partagé par base</b> : une seule lecture du tenant à la fois (« en vol »), quel que soit le nombre de
///   salariés qui la demandent ; les suivants attendent la même lecture. Le résultat est conservé <see cref="Duree"/> en mémoire,
///   à l'INTÉRIEUR de cette classe : aucun membre public ne le rend, et chaque lecture n'extrait que la clé du demandeur.</item>
///   <item><b>Entrée par salarié</b> (clé utilisateur | base | matricule) : la part du salarié, copiée, jamais servie à un autre
///   utilisateur ni à un autre matricule ; elle n'est jamais plus vieille que le chargement dont elle vient.</item>
/// </list>
/// Un échec n'est jamais conservé (la lecture suivante réessaie). Plafond mémoire : au plus <see cref="MaxBases"/> chargements
/// de tenant et <see cref="MaxEntrees"/> parts de salarié.
/// </summary>
public sealed class SelfLeaveCache
{
    public static readonly TimeSpan Duree = TimeSpan.FromMinutes(5);
    public const int MaxEntrees = 512;
    public const int MaxBases = 8;

    private sealed record Part(DateTimeOffset Expire, List<LeaveDay> Valeur);

    private sealed record Chargement(DateTimeOffset Expire, Dictionary<string, List<LeaveDay>> ParCle);

    private readonly ConcurrentDictionary<string, Part> _entrees = new();
    private readonly ConcurrentDictionary<string, Chargement> _tenants = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<Dictionary<string, List<LeaveDay>>>>> _vols = new();

    public static string Cle(decimal userId, string sageDb, string cleMatricule)
        => string.Join('|', ((long)userId).ToString(), CleBase(sageDb), cleMatricule);

    private static string CleBase(string sageDb) => sageDb.Trim().ToUpperInvariant();

    /// <summary>Nombre de lectures du tenant effectivement lancées (mesure et tests).</summary>
    public int LecturesTenant => Volatile.Read(ref _lectures);

    private int _lectures;

    /// <summary>
    /// Congés du salarié <paramref name="cleMatricule"/> pour l'utilisateur <paramref name="userId"/> sur la base
    /// <paramref name="sageDb"/>. <paramref name="chargerTenant"/> lit tout le tenant (clé canonique de matricule → congés) ;
    /// elle est appelée au plus une fois à la fois par base, sans jeton d'annulation d'un appelant particulier : l'appelant qui
    /// la déclenche attend sa fin, les autres attendent la même lecture (et peuvent, eux, être annulés via <paramref name="ct"/>).
    /// </summary>
    public async Task<List<LeaveDay>> ObtenirAsync(decimal userId, string sageDb, string cleMatricule, TimeProvider horloge,
        Func<Task<Dictionary<string, List<LeaveDay>>>> chargerTenant, CancellationToken ct = default)
    {
        var cle = Cle(userId, sageDb, cleMatricule);
        if (_entrees.TryGetValue(cle, out var e) && e.Expire > horloge.GetUtcNow())
            return e.Valeur;

        var tenant = await TenantAsync(CleBase(sageDb), horloge, chargerTenant, ct);
        var siens = tenant.ParCle.TryGetValue(cleMatricule, out var l) ? new List<LeaveDay>(l) : new List<LeaveDay>();

        var maintenant = horloge.GetUtcNow();
        foreach (var (k, v) in _entrees)
            if (v.Expire <= maintenant)
                _entrees.TryRemove(k, out _);
        while (_entrees.Count >= MaxEntrees && _entrees.Keys.FirstOrDefault() is string ancienne)
            _entrees.TryRemove(ancienne, out _);
        var expire = maintenant + Duree;
        _entrees[cle] = new Part(expire < tenant.Expire ? expire : tenant.Expire, siens);
        return siens;
    }

    private async Task<Chargement> TenantAsync(string cleBase, TimeProvider horloge, Func<Task<Dictionary<string, List<LeaveDay>>>> charger, CancellationToken ct)
    {
        if (_tenants.TryGetValue(cleBase, out var c) && c.Expire > horloge.GetUtcNow())
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
            return _tenants.TryGetValue(cleBase, out c) && c.ParCle == tout ? c : new Chargement(horloge.GetUtcNow() + Duree, tout);
        }

        try
        {
            // Une lecture vient de se terminer entre le premier contrôle et l'inscription : inutile d'en lancer une autre.
            if (_tenants.TryGetValue(cleBase, out c) && c.Expire > horloge.GetUtcNow())
                return c;

            // Le déclencheur attend la fin de la lecture sans jeton d'annulation : sa portée (requête) reste vivante tant que
            // d'autres salariés en dépendent.
            var resultat = await vol.Value;
            var charge = new Chargement(horloge.GetUtcNow() + Duree, resultat);
            foreach (var (k, v) in _tenants)
                if (v.Expire <= horloge.GetUtcNow())
                    _tenants.TryRemove(k, out _);
            while (_tenants.Count >= MaxBases && _tenants.OrderBy(t => t.Value.Expire).Select(t => t.Key).FirstOrDefault() is string vieille)
                _tenants.TryRemove(vieille, out _);
            _tenants[cleBase] = charge;
            return charge;
        }
        finally
        {
            _vols.TryRemove(new KeyValuePair<string, Lazy<Task<Dictionary<string, List<LeaveDay>>>>>(cleBase, vol));
        }
    }
}
