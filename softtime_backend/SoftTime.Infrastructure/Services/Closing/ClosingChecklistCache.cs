using System.Collections.Concurrent;
using System.Globalization;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Cache mémoire de la checklist de clôture : la clé porte les bases, la période, le département et le périmètre de
/// l'utilisateur, si bien que deux utilisateurs ne partagent jamais une entrée. Enregistré en singleton.
/// </summary>
public sealed class ClosingChecklistCache : IClosingChecklistCache
{
    private const int MaxEntrees = 16;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expire, ClosingChecklistDto Valeur)> _entrees = new();

    public bool TryGet(string cle, DateTimeOffset maintenant, out ClosingChecklistDto valeur)
    {
        if (_entrees.TryGetValue(cle, out var e) && e.Expire > maintenant)
        {
            valeur = e.Valeur;
            return true;
        }
        valeur = null!;
        return false;
    }

    public void Set(string cle, ClosingChecklistDto valeur, DateTimeOffset maintenant, TimeSpan duree)
    {
        foreach (var (k, e) in _entrees)
            if (e.Expire <= maintenant)
                _entrees.TryRemove(k, out _);
        while (_entrees.Count >= MaxEntrees && _entrees.Keys.FirstOrDefault() is string ancienne)
            _entrees.TryRemove(ancienne, out _);
        _entrees[cle] = (maintenant + duree, valeur);
    }

    /// <summary>Clé d'une demande dont la période par défaut est déjà appliquée.</summary>
    internal static string Cle(ClosingChecklistRequest request, string sageDb, string pointeuseDb)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Join('|',
            sageDb.Trim().ToUpperInvariant(), pointeuseDb.Trim().ToUpperInvariant(),
            request.From.ToString("yyyy-MM-dd", c), request.To.ToString("yyyy-MM-dd", c),
            (request.Departement ?? string.Empty).Trim().ToUpperInvariant(), request.Perimetre);
    }
}
