using System.Globalization;

namespace SoftTime.Domain.Services;

/// <summary>
/// Regroupement temporel du tableau de bord analytique (valeurs de <c>periode</c> :
/// quotidienne, hebdomadaire, mensuelle).
///
/// Chaque groupe expose deux choses : une <b>clé triable</b> (la date qui commence la période)
/// et un <b>libellé lisible</b>. Trier sur le libellé était faux — « 1-2026 » passait avant
/// « 27-2025 » — et une semaine ne peut pas se résumer à « 27-2025 » : la semaine ISO va du
/// lundi au dimanche et son numéro d'année n'est pas celui du calendrier (la semaine 1 de 2026
/// commence le 29/12/2025).
/// </summary>
public static class PeriodGrouping
{
    public const string Quotidienne = "quotidienne";
    public const string Hebdomadaire = "hebdomadaire";
    public const string Mensuelle = "mensuelle";

    /// <summary>Retourne la clé de tri chronologique et le libellé du groupe contenant <paramref name="date"/>.</summary>
    public static (DateTime Cle, string Libelle) Grouper(DateTime date, string periode) => periode switch
    {
        Quotidienne => (date.Date, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        Hebdomadaire => SemaineIso(date),
        _ => (new DateTime(date.Year, date.Month, 1), $"{date.Month:00}-{date.Year}")
    };

    /// <summary>
    /// Toutes les périodes balayées par <paramref name="from"/> → <paramref name="to"/>, dans
    /// l'ordre chronologique et sans trou. Le dashboard s'en sert pour afficher 0 au lieu d'omettre
    /// une semaine/mois sans événement : sans cela les courbes et les tableaux ne sont plus alignés
    /// (deux séries issues de requêtes différentes n'ont pas les mêmes trous).
    /// </summary>
    public static IReadOnlyList<(DateTime Cle, string Libelle)> Clefs(DateTime from, DateTime to, string periode)
    {
        var cles = new List<(DateTime Cle, string Libelle)>();
        if (to.Date < from.Date)
            return cles;

        var curseur = from.Date;
        while (curseur <= to.Date)
        {
            var cle = Grouper(curseur, periode);
            if (cles.Count == 0 || cles[^1].Cle != cle.Cle)
                cles.Add(cle);
            curseur = periode switch
            {
                Quotidienne => curseur.AddDays(1),
                Hebdomadaire => cle.Cle.AddDays(7),
                _ => cle.Cle.AddMonths(1)
            };
        }
        return cles;
    }

    /// <summary>Lundi de la semaine ISO, avec un libellé « Semaine 27 (30/06 - 06/07/2025) ».</summary>
    private static (DateTime Cle, string Libelle) SemaineIso(DateTime date)
    {
        var lundi = date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        var dimanche = lundi.AddDays(6);
        var numero = ISOWeek.GetWeekOfYear(lundi);
        var debut = lundi.ToString("dd/MM", CultureInfo.InvariantCulture);
        var fin = dimanche.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return (lundi, $"Semaine {numero} ({debut} - {fin})");
    }
}
