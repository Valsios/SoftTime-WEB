using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Services;

/// <summary>
/// Cadre commun des rapports d'absences et de retards : période effective (plafonnée à la veille), jours
/// fériés et planning retenu par (clé matricule, jour).
/// </summary>
internal sealed record ReportCalendar(
    DateTime Debut,
    DateTime Fin,
    IReadOnlySet<DateTime> Feries,
    IReadOnlyDictionary<(string Cle, DateTime Date), PlanningSalarie> Plannings)
{
    public bool Contient(DateTime jour) => jour >= Debut && jour <= Fin;

    public PlanningSalarie? Planning(string cle, DateTime jour) =>
        Plannings.TryGetValue((cle, jour), out var plan) ? plan : null;
}
