using System.Collections.Concurrent;
using SoftTime.Domain.Services;

namespace SoftTime.Application.SelfService;

/// <summary>
/// Cache mémoire des congés SAGE d'UN salarié. La source SAGE n'offre aucune lecture filtrée : charger les congés coûte
/// plusieurs secondes (tout le tenant). Pour ne payer ce coût qu'une fois par salarié, seule la part du salarié est conservée,
/// sous une clé qui contient l'utilisateur, la base et son matricule : une entrée n'est jamais servie à quelqu'un d'autre.
/// Les chargements simultanés d'une même clé n'en font qu'un (verrou par clé). Un échec n'est jamais conservé.
/// </summary>
public sealed class SelfLeaveCache
{
    public static readonly TimeSpan Duree = TimeSpan.FromMinutes(5);
    private const int MaxEntrees = 512;

    private readonly ConcurrentDictionary<string, (DateTimeOffset Expire, List<LeaveDay> Valeur)> _entrees = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _verrous = new();

    public static string Cle(decimal userId, string sageDb, string cleMatricule)
        => string.Join('|', ((long)userId).ToString(), sageDb.Trim().ToUpperInvariant(), cleMatricule);

    public async Task<List<LeaveDay>> ObtenirAsync(string cle, TimeProvider horloge, Func<Task<List<LeaveDay>>> charger)
    {
        if (_entrees.TryGetValue(cle, out var e) && e.Expire > horloge.GetUtcNow())
            return e.Valeur;

        var verrou = _verrous.GetOrAdd(cle, _ => new SemaphoreSlim(1, 1));
        await verrou.WaitAsync();
        try
        {
            var maintenant = horloge.GetUtcNow();
            if (_entrees.TryGetValue(cle, out e) && e.Expire > maintenant)
                return e.Valeur;

            var valeur = await charger();
            foreach (var (k, v) in _entrees)
                if (v.Expire <= maintenant)
                    _entrees.TryRemove(k, out _);
            while (_entrees.Count >= MaxEntrees && _entrees.Keys.FirstOrDefault() is string ancienne)
                _entrees.TryRemove(ancienne, out _);
            _entrees[cle] = (maintenant + Duree, valeur);
            return valeur;
        }
        finally
        {
            verrou.Release();
        }
    }
}
