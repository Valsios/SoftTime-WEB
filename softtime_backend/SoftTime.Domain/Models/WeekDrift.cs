namespace SoftTime.Domain.Models;

/// <summary>
/// Écart habituel d'un salarié par rapport à son planning, moyenne des dernières semaines
/// entièrement pointées : heures créditées et heures supplémentaires, réel moins planifié.
/// </summary>
/// <param name="Heures">Écart moyen hebdomadaire des heures créditées (mode multi-pointage).</param>
/// <param name="HeuresSup">Écart moyen hebdomadaire des heures supplémentaires (mode 2 pointages).</param>
/// <param name="Semaines">Nombre de semaines qui ont servi à la moyenne (0 : aucun historique).</param>
public sealed record WeekDrift(decimal Heures, decimal HeuresSup, int Semaines)
{
    public static WeekDrift Aucune { get; } = new(0m, 0m, 0);
}
