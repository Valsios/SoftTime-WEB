using System.Globalization;

using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Signaux de vigilance avant la paie, de niveau « Information » par défaut (poids 0 : sans effet sur le score ni le
/// statut). Règles pures sur des listes déjà chargées, seuils lus dans <see cref="ClosingRulesOptions"/> (valeurs par
/// défaut LARGES : seuls les cas nettement hors norme ressortent). Aucune lecture, aucune configuration.
/// </summary>
public static class PayrollVigilanceRules
{
    /// <summary>
    /// Corrections manuelles massives : un salarié dont les jours corrigés à la main (<c>ORIGINE = MANUEL</c>) sur la
    /// période atteignent À LA FOIS le minimum (<see cref="ClosingRulesOptions.CorrectionsManuellesMinimum"/>) et la
    /// part de ses jours contrôlés (<see cref="ClosingRulesOptions.CorrectionsManuellesRatio"/>). Une anomalie par
    /// salarié, datée de son dernier jour corrigé à la main.
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> CorrectionsManuelles(
        IEnumerable<HeureCorrigee> corrections,
        IReadOnlySet<(string Cle, DateTime Jour)> joursControles,
        ClosingScope scope)
    {
        var options = scope.Options ?? ClosingRulesOptions.Default;
        var controlesParCle = joursControles
            .GroupBy(j => j.Cle, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var resultats = corrections
            .Where(h => h.DatePointageIn is DateTime d && d.Date >= scope.From.Date && d.Date <= scope.To.Date)
            .Where(h => string.Equals(h.Origine?.Trim(), ClosingAnomalyFactory.OrigineManuelle, StringComparison.OrdinalIgnoreCase))
            .Select(h => (Cle: MatriculeIdentity.Cle(h.MatriculeSage), Jour: h.DatePointageIn!.Value.Date))
            .Where(x => x.Cle.Length > 0)
            .Distinct()
            .GroupBy(x => x.Cle, StringComparer.Ordinal)
            .Select(g => (Cle: g.Key, Jours: g.Count(), Dernier: g.Max(x => x.Jour), Controles: controlesParCle.GetValueOrDefault(g.Key)))
            .Where(x => x.Jours >= options.CorrectionsManuellesMinimum
                        && x.Jours >= options.CorrectionsManuellesRatio * x.Controles)
            .Select(x => ClosingAnomalyFactory.Anomalie(
                ClosingAnomalyType.CorrectionsManuelles, x.Cle, x.Dernier,
                DetailCorrections(x.Jours, x.Controles, options),
                gravite: options.Gravite(ClosingAnomalyType.CorrectionsManuelles)))
            .ToList();
        return PayrollClosingRules.Trier(resultats);
    }

    /// <summary>
    /// Heures supplémentaires atypiques : semaine VALIDÉE dont les heures supplémentaires dépassent strictement
    /// <see cref="ClosingRulesOptions.HeuresSupHebdoSeuil"/>. Une semaine appartient à la période qui contient son
    /// dimanche (même règle que l'exonération) ; l'anomalie est datée de ce dimanche. Une ligne par salarié et par
    /// semaine (doublons de validation ignorés, plus petit ID retenu).
    /// </summary>
    public static IReadOnlyList<ClosingAnomaly> HeuresSupAtypiques(IEnumerable<HsValidee> semaines, ClosingScope scope)
    {
        var options = scope.Options ?? ClosingRulesOptions.Default;
        var resultats = semaines
            .Where(v => v.ValidationHs == true && v.DateDeb.HasValue && v.Hs is decimal hs && hs > options.HeuresSupHebdoSeuil)
            .Select(v => (Cle: MatriculeIdentity.Cle(v.Matricules), Lundi: v.DateDeb!.Value.Date, v.Id, Hs: v.Hs!.Value))
            .Where(x => x.Cle.Length > 0 && x.Lundi.AddDays(6) >= scope.From.Date && x.Lundi.AddDays(6) <= scope.To.Date)
            .GroupBy(x => (x.Cle, x.Lundi))
            .Select(g => g.OrderBy(x => x.Id).First())
            .Select(x => ClosingAnomalyFactory.Anomalie(
                ClosingAnomalyType.HeuresSupAtypiques, x.Cle, x.Lundi.AddDays(6),
                DetailHeuresSup(x.Lundi, x.Hs, options), x.Hs,
                gravite: options.Gravite(ClosingAnomalyType.HeuresSupAtypiques)))
            .ToList();
        return PayrollClosingRules.Trier(resultats);
    }

    private static string DetailCorrections(int jours, int controles, ClosingRulesOptions options)
    {
        var part = (int)Math.Round(options.CorrectionsManuellesRatio * 100m, MidpointRounding.AwayFromZero);
        return $"{jours} jour(s) corrigé(s) à la main sur {controles} jour(s) contrôlé(s) "
               + $"(seuil : {part} % et au moins {options.CorrectionsManuellesMinimum} jours)";
    }

    private static string DetailHeuresSup(DateTime lundi, decimal hs, ClosingRulesOptions options)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        return $"semaine du {lundi.ToString("dd/MM", fr)} au {lundi.AddDays(6).ToString("dd/MM", fr)} : "
               + $"{hs.ToString("0.##", fr)} h supplémentaires validées (seuil : {options.HeuresSupHebdoSeuil.ToString("0.##", fr)} h)";
    }
}
