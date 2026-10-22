namespace SoftTime.Domain.Services;

/// <summary>
/// Règles de qualification d'une journée, partagées par le calcul des heures supplémentaires et
/// le tableau de bord, afin que les deux écrans comptent les mêmes journées.
/// </summary>
public static class JourneeRules
{
    /// <summary>Au-delà de 12 h, une vacation est considérée comme un poste de nuit.</summary>
    public static readonly TimeSpan SeuilNuitPoste = TimeSpan.FromHours(12);

    /// <summary>
    /// Fin de vacation normalisée : quand l'heure de fin est antérieure ou égale au début, elle
    /// appartient au lendemain. Les deux conventions de stockage coexistent dans
    /// <c>T_PLANNING_SAL</c> (générateur CSV : `HD` le lendemain, saisie manuelle : même jour).
    /// </summary>
    public static DateTime? NormaliserFin(DateTime? ha, DateTime? hd)
    {
        if (ha is null || hd is null)
            return null;
        return hd.Value <= ha.Value ? hd.Value.AddDays(1) : hd.Value;
    }

    /// <summary>La vacation planifiée franchit-elle minuit ?</summary>
    public static bool EstNuit(DateTime? ha, DateTime? hd)
    {
        var fin = NormaliserFin(ha, hd);
        return ha is not null && fin is not null && fin.Value - ha.Value > SeuilNuitPoste;
    }

    /// <summary>
    /// Le jour est-il hors planning ? Un week-end ou un jour <c>OffP</c> l'est toujours (comportement
    /// historique : ni heure ni anomalie). Une <b>absence de ligne de planning</b> ne l'est jamais :
    /// les salariés sans planning doivent continuer à être pointés comme avant. L'option
    /// <c>ANOMALIE_HORS_PLANNING</c> ne signale que les trous d'un planning existant.
    /// </summary>
    public static bool EstHorsPlanning(
        DateTime jour,
        bool aLignePlanning,
        bool ligneOff,
        bool carteAPlanning,
        bool signalerLesTrous)
    {
        if (jour.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || ligneOff)
            return true;
        return signalerLesTrous && !aLignePlanning && carteAPlanning;
    }

    /// <summary>
    /// Un jour planifié sans heure corrigée est-il une absence ?
    /// Un jour de congé SAGE n'en est jamais une. En mode strict, un jour en attente d'arbitrage
    /// (anomalie non convertie) n'est ni travaillé ni absent : il ne doit pas gonfler l'absentéisme
    /// avant d'être traité.
    /// </summary>
    public static bool EstAbsence(
        bool jourPlanifie,
        bool estConge,
        bool aCorrection,
        bool aAnomalie,
        bool modeStrict)
    {
        if (!jourPlanifie || estConge || aCorrection)
            return false;
        if (modeStrict && aAnomalie)
            return false;
        return true;
    }
}
