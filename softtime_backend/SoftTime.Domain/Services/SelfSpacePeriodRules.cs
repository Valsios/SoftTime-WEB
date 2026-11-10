namespace SoftTime.Domain.Services;

/// <summary>
/// Période consultable dans « Mon espace » : par défaut le mois en cours jusqu'à aujourd'hui ; au plus
/// <see cref="FenetreMois"/> mois, dans la fenêtre des <see cref="FenetreMois"/> derniers mois (mois en cours compris),
/// sans jour futur. Une période invalide est une <see cref="DomainException"/> (400).
/// </summary>
public static class SelfSpacePeriodRules
{
    /// <summary>Fenêtre consultable, en mois (mois en cours compris) et durée maximale d'une période
    /// demandée.</summary>
    public const int FenetreMois = 4;

    /// <summary>Période par défaut : le mois en cours jusqu'à aujourd'hui.</summary>
    public static (DateTime From, DateTime To) PeriodeParDefaut(DateTime aujourdhui)
        => (new DateTime(aujourdhui.Year, aujourdhui.Month, 1), aujourdhui);

    /// <summary>Borne la période demandée ; la fin est ramenée à aujourd'hui si elle est dans le futur.</summary>
    public static (DateTime From, DateTime To) Borner(DateTime? from, DateTime? to, DateTime aujourdhui)
    {
        var (debutParDefaut, finParDefaut) = PeriodeParDefaut(aujourdhui);
        var debut = (from ?? debutParDefaut).Date;
        var fin = (to ?? finParDefaut).Date;
        if (fin < debut)
            throw new DomainException("La date de fin doit être postérieure ou égale à la date de début.");
        var fenetre = new DateTime(aujourdhui.Year, aujourdhui.Month, 1).AddMonths(-(FenetreMois - 1));
        if (debut < fenetre)
            throw new DomainException($"Seuls les {FenetreMois} derniers mois sont consultables.");
        if (fin > aujourdhui)
            fin = aujourdhui;
        if (debut > fin)
            throw new DomainException("La période demandée est dans le futur.");
        if (fin > debut.AddMonths(FenetreMois).AddDays(-1))
            throw new DomainException($"La période ne peut pas dépasser {FenetreMois} mois.");
        return (debut, fin);
    }

    /// <summary>Lundi de la semaine du jour donné.</summary>
    public static DateTime LundiDe(DateTime jour) => jour.Date.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
}
