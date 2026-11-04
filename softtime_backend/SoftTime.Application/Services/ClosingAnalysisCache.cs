using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;

namespace SoftTime.Application.Services;

/// <summary>
/// Cache mémoire de courte durée de l'analyse de clôture (partie indépendante de la page). La clé contient
/// la base SAGE, la base pointeuse, la période, le département, la comparaison et toutes les valeurs de règles :
/// deux demandes qui donneraient des résultats différents n'ont jamais la même clé. Aucune écriture n'existant
/// dans la clôture, l'invalidation est l'expiration (durée courte) ou une nouvelle analyse, qui remplace l'entrée.
/// </summary>
internal sealed class ClosingAnalysisCache
{
    private const int MaxEntrees = 16;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expire, AnalysePreparee Valeur)> _entrees = new();

    public bool TryGet(string cle, DateTimeOffset maintenant, out AnalysePreparee valeur)
    {
        if (_entrees.TryGetValue(cle, out var e) && e.Expire > maintenant)
        {
            valeur = e.Valeur;
            return true;
        }
        valeur = null!;
        return false;
    }

    public void Set(string cle, AnalysePreparee valeur, DateTimeOffset maintenant, TimeSpan duree)
    {
        foreach (var (k, e) in _entrees)
            if (e.Expire <= maintenant)
                _entrees.TryRemove(k, out _);
        while (_entrees.Count >= MaxEntrees && _entrees.Keys.FirstOrDefault() is string ancienne)
            _entrees.TryRemove(ancienne, out _);
        _entrees[cle] = (maintenant + duree, valeur);
    }

    public static string Cle(string sageDb, string pointeuseDb, ClosingAnalysisRequest r)
    {
        var o = r.Options ?? ClosingRulesOptions.Default;
        var c = CultureInfo.InvariantCulture;
        var s = new StringBuilder()
            .Append(sageDb.Trim().ToUpperInvariant()).Append('|').Append(pointeuseDb.Trim().ToUpperInvariant()).Append('|')
            .Append(r.From.Date.ToString("yyyy-MM-dd", c)).Append('|').Append(r.To.Date.ToString("yyyy-MM-dd", c)).Append('|')
            .Append((r.Departement ?? string.Empty).Trim().ToUpperInvariant()).Append('|')
            .Append(r.IncludeComparison ? '1' : '0').Append('|')
            .Append(o.DuplicateToleranceSeconds).Append(',').Append(o.IgnorerSemainesValidees).Append(',')
            .Append(o.ExclureSansPlanningSurPeriode).Append(',').Append(o.DoublonsMonoGravite).Append(',')
            .Append(o.PoidsInformation).Append(',').Append(o.PoidsMineure).Append(',').Append(o.PoidsImportante).Append(',')
            .Append(o.PoidsCritique).Append(',').Append(o.CriticalRatioBlocking.ToString(c)).Append('|');
        foreach (var (type, gravite) in o.Gravites.OrderBy(g => g.Key))
            s.Append(type).Append('=').Append(gravite).Append(';');
        return s.ToString();
    }
}
