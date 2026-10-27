using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>Synthèse d'une journée de travail à partir de ses paires entrée/sortie.</summary>
public sealed record CorrectionDaySummary(
    TimeSpan? Entree,
    TimeSpan? Sortie,
    TimeSpan HeuresTravaillees,
    TimeSpan HeuresNuit,
    int NombrePaires);

/// <summary>
/// Agrège les paires d'une même journée. Le pré-traitement multi-pointage peut produire plusieurs
/// paires (pause déjeuner) : les calculs aval doivent sommer leurs durées au lieu de ne retenir que
/// la première correction.
/// </summary>
public static class CorrectionDayRules
{
    /// <summary>
    /// Somme les durées des paires. La pause de catégorie n'est retranchée que lorsqu'il n'y a
    /// qu'une paire : avec plusieurs paires, l'écart entre elles représente déjà la pause non payée.
    /// </summary>
    public static CorrectionDaySummary Summarize(IReadOnlyList<PunchPair> pairs, TimeSpan pause = default)
    {
        if (pairs.Count == 0)
            return new CorrectionDaySummary(null, null, TimeSpan.Zero, TimeSpan.Zero, 0);

        if (pause < TimeSpan.Zero)
            pause = TimeSpan.Zero;

        var total = TimeSpan.Zero;
        var nuit = TimeSpan.Zero;
        DateTime? premiere = null;
        DateTime? derniere = null;

        foreach (var paire in pairs)
        {
            var debut = paire.EntreeDate.Date + paire.EntreeHeure;
            var fin = paire.SortieDate.Date + paire.SortieHeure;
            var duree = fin - debut;
            if (duree < TimeSpan.Zero)
                duree = TimeSpan.Zero;

            total += duree;
            if (paire.SortieDate.Date > paire.EntreeDate.Date
                || OvertimeRules.EstPlageNuit(paire.EntreeHeure, paire.SortieHeure))
                nuit += duree;

            if (premiere is null || debut < premiere)
                premiere = debut;
            if (derniere is null || fin > derniere)
                derniere = fin;
        }

        if (pairs.Count == 1)
        {
            total -= pause;
            if (total < TimeSpan.Zero)
                total = TimeSpan.Zero;
        }

        return new CorrectionDaySummary(
            premiere?.TimeOfDay,
            derniere?.TimeOfDay,
            total,
            nuit,
            pairs.Count);
    }
}
