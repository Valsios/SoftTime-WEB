namespace SoftTime.Infrastructure.Services.Dashboard;

/// <summary>Événements de la période, une ligne par salarié et par journée.</summary>
internal sealed record DashboardEvents(
    List<(string Cle, DateTime Date)> Absences,
    List<(string Cle, DateTime Date)> Presences,
    List<(string Cle, DateTime Date, int Minutes)> Retards,
    int JoursPlanifies)
{
    public int MinutesRetard => Retards.Sum(r => r.Minutes);
}
