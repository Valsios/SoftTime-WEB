using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règles de congés portées depuis le desktop (<c>Calcul_HSService.LoadCongesForMatricule</c>) :
/// <list type="bullet">
///   <item>les codes d'événement <c>0200</c> et <c>0300</c> ne sont pas des congés ;</item>
///   <item>les dimanches ne sont jamais comptés ;</item>
///   <item>une période qui commence l'après-midi (<c>ApresMidi</c>) donne une demi-journée le 1er jour ;</item>
///   <item>une période qui finit le matin (<c>Matin</c>) donne une demi-journée le dernier jour ;</item>
///   <item>un événement d'un seul jour avec un drapeau matin ou après-midi vaut une demi-journée.</item>
/// </list>
/// Classe pure (aucune dépendance EF) afin d'être testable unitairement, sur le modèle de
/// <see cref="OvertimeRules"/>.
/// </summary>
public static class LeaveRules
{
    private static readonly HashSet<string> CodesNonConge =
        new(StringComparer.OrdinalIgnoreCase) { "0200", "0300" };

    /// <summary>Ce code d'événement SAGE représente-t-il un congé (hors codes exclus) ?</summary>
    public static bool EstCodeConge(string? code)
        => !string.IsNullOrWhiteSpace(code) && !CodesNonConge.Contains(code.Trim());

    /// <summary>
    /// Développe les périodes de congé en jours, en appliquant les demi-journées et l'exclusion du
    /// dimanche. Les jours produits ne sont pas dédoublonnés : une période peut en chevaucher une autre.
    /// </summary>
    public static IReadOnlyList<LeaveDay> Expandre(IEnumerable<LeavePeriod> periodes)
    {
        var jours = new List<LeaveDay>();
        foreach (var periode in periodes)
        {
            var debut = periode.Start.Date;
            var fin = periode.End.Date;
            var nbJours = (int)(fin - debut).TotalDays;
            if (nbJours >= 1)
            {
                if (periode.ApresMidi)
                {
                    // Le congé commence l'après-midi du premier jour.
                    jours.Add(new LeaveDay(debut, 0.5m, periode.Code));
                    for (var i = 1; i <= nbJours; i++)
                        Ajouter(jours, debut.AddDays(i), periode.Code);
                }
                else if (periode.Matin)
                {
                    // Le congé se termine le matin du dernier jour.
                    for (var i = 0; i < nbJours; i++)
                        Ajouter(jours, debut.AddDays(i), periode.Code);
                    jours.Add(new LeaveDay(fin, 0.5m, periode.Code));
                }
                else
                {
                    for (var i = 0; i <= nbJours; i++)
                        Ajouter(jours, debut.AddDays(i), periode.Code);
                }
            }
            else
            {
                var valeur = periode.Matin || periode.ApresMidi ? 0.5m : 1m;
                jours.Add(new LeaveDay(debut, valeur, periode.Code));
            }
        }
        return jours;
    }

    /// <summary>L'employé est-il en congé ce jour-là ?</summary>
    public static bool EstConge(IEnumerable<LeaveDay> jours, DateTime date)
    {
        var cible = date.Date;
        return jours.Any(j => j.Date.Date == cible);
    }

    /// <summary>
    /// Valeur du congé ce jour-là (1 = journée, 0.5 = demi-journée, 0 = pas de congé).
    /// En cas de chevauchement de deux périodes, la valeur la plus forte est retenue.
    /// </summary>
    public static decimal Valeur(IEnumerable<LeaveDay> jours, DateTime date)
    {
        var cible = date.Date;
        return jours.Where(j => j.Date.Date == cible).Select(j => j.Valeur).DefaultIfEmpty(0m).Max();
    }

    private static void Ajouter(List<LeaveDay> jours, DateTime date, string? code)
    {
        if (date.DayOfWeek != DayOfWeek.Sunday)
            jours.Add(new LeaveDay(date, 1m, code));
    }
}
