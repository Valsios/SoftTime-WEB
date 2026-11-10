namespace SoftTime.Domain.Models;

/// <summary>
/// Fenêtre de planning d'une journée, indépendante d'EF. <see cref="Debut"/> et <see cref="Fin"/>
/// sont les heures planifiées ; <see cref="Off"/> indique un jour non travaillé.
/// </summary>
public sealed record PunchPlanningWindow(DateTime Jour, DateTime? Debut, DateTime? Fin, bool Off)
{
    /// <summary>
    /// Un poste est de nuit quand la sortie tombe un autre jour, ou quand l'heure de fin est
    /// antérieure ou égale à l'heure de début (nuit saisie sur la même date).
    /// </summary>
    public bool EstNuit =>
        Debut.HasValue
        && Fin.HasValue
        && (Fin.Value.Date > Debut.Value.Date
            || (Fin.Value.Date == Debut.Value.Date && Fin.Value.TimeOfDay <= Debut.Value.TimeOfDay));

    /// <summary>Date/heure de fin réelle, la sortie de nuit étant reportée au lendemain si besoin.</summary>
    public DateTime? FinEffective
    {
        get
        {
            if (!Debut.HasValue || !Fin.HasValue)
                return null;
            if (Fin.Value.Date > Debut.Value.Date)
                return Fin.Value;
            if (Fin.Value.Date == Debut.Value.Date && Fin.Value.TimeOfDay <= Debut.Value.TimeOfDay)
                return Fin.Value.Date.AddDays(1).Add(Fin.Value.TimeOfDay);
            return Fin.Value;
        }
    }
}
