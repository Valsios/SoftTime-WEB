using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Cache mémoire de l'analyse de clôture (partie indépendante de la page). La clé contient la base SAGE, la base
/// pointeuse, la période, le département, la comparaison, le périmètre de l'utilisateur et toutes les valeurs de
/// règles : deux demandes qui donneraient des résultats différents, ou venant d'utilisateurs différents, n'ont jamais
/// la même clé.
/// <para>
/// Durée de vie : <b>glissante</b> (chaque lecture avec réutilisation la renouvelle, pour la durée glissante) mais
/// jamais au-delà d'un <b>plafond absolu</b> mesuré depuis le calcul d'origine. Une nouvelle analyse remplace l'entrée
/// et repart d'un plafond neuf. Aucune écriture n'existant dans la clôture, l'invalidation est l'expiration ou une
/// nouvelle analyse. Enregistré en singleton : partagé par toutes les requêtes du processus.
/// </para>
/// </summary>
public sealed class ClosingAnalysisCache
{
    private const int MaxEntrees = 16;
    private readonly ConcurrentDictionary<string, Entree> _entrees = new();

    private sealed record Entree(DateTimeOffset Expire, DateTimeOffset Plafond, AnalysePreparee Valeur);

    /// <summary>Entrée valide (ni expirée ni au-delà du plafond) : renouvelle son délai glissant, sans dépasser le
    /// plafond.</summary>
    internal bool TryGet(string cle, DateTimeOffset maintenant, TimeSpan dureeGlissante, out AnalysePreparee valeur)
    {
        if (_entrees.TryGetValue(cle, out var e) && e.Expire > maintenant && e.Plafond > maintenant)
        {
            var expire = maintenant + dureeGlissante;
            if (expire > e.Plafond)
                expire = e.Plafond;
            // Comparaison avec l'entrée lue : une analyse neuve posée entre-temps n'est jamais écrasée par un
            // renouvellement.
            _entrees.TryUpdate(cle, e with { Expire = expire }, e);
            valeur = e.Valeur;
            return true;
        }
        valeur = null!;
        return false;
    }

    /// <summary>Pose (ou remplace) l'entrée d'une analyse calculée à neuf : plafond absolu mesuré depuis
    /// maintenant.</summary>
    internal void Set(string cle, AnalysePreparee valeur, DateTimeOffset maintenant, ClosingCacheWindow fenetre)
    {
        foreach (var (k, e) in _entrees)
            if (e.Expire <= maintenant || e.Plafond <= maintenant)
                _entrees.TryRemove(k, out _);
        while (_entrees.Count >= MaxEntrees && _entrees.Keys.FirstOrDefault() is string ancienne)
            _entrees.TryRemove(ancienne, out _);
        var limite = maintenant + fenetre.Plafond;
        var expire = maintenant + fenetre.Glissant;
        _entrees[cle] = new Entree(expire > limite ? limite : expire, limite, valeur);
    }

    internal static string Cle(string sageDb, string pointeuseDb, ClosingAnalysisRequest r)
    {
        var o = r.Options ?? ClosingRulesOptions.Default;
        var c = CultureInfo.InvariantCulture;
        var s = new StringBuilder()
            .Append(sageDb.Trim().ToUpperInvariant()).Append('|').Append(pointeuseDb.Trim().ToUpperInvariant()).Append('|')
            .Append(r.From.Date.ToString("yyyy-MM-dd", c)).Append('|').Append(r.To.Date.ToString("yyyy-MM-dd", c)).Append('|')
            .Append((r.Departement ?? string.Empty).Trim().ToUpperInvariant()).Append('|')
            .Append(r.IncludeComparison ? '1' : '0').Append('|')
            .Append(r.Perimetre ?? string.Empty).Append('|')
            .Append(o.DuplicateToleranceSeconds).Append(',').Append(o.IgnorerSemainesValidees).Append(',')
            .Append(o.ExclureSansPlanningSurPeriode).Append(',').Append(o.DoublonsMonoGravite).Append(',')
            .Append(o.PoidsInformation).Append(',').Append(o.PoidsMineure).Append(',').Append(o.PoidsImportante).Append(',')
            .Append(o.PoidsCritique).Append(',').Append(o.CriticalRatioBlocking.ToString(c)).Append('|');
        foreach (var (type, gravite) in o.Gravites.OrderBy(g => g.Key))
            s.Append(type).Append('=').Append(gravite).Append(';');
        return s.ToString();
    }
}
