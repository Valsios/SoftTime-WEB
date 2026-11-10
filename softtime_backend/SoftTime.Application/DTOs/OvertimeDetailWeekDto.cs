namespace SoftTime.Application.DTOs;

/// <summary>
/// Semaine du calcul EXO/IMPO d'un salarié (répartition 130 % / 150 %). <see cref="Debut"/> et
/// <see cref="Fin"/> sont les bornes lundi → dimanche de la semaine ; la période du calcul, elle,
/// est affichée dans l'en-tête du détail et stockée dans <c>T_HSSemaine.PeriodeDebut/PeriodeFin</c>.
/// </summary>
public record OvertimeDetailWeekDto(int? Semaine, DateTime? Debut, DateTime? Fin, decimal Hs, decimal Hs1, decimal Hs2)
{
    public OvertimeDetailWeekDto() : this(default, default, default, default, default, default) { }
}
