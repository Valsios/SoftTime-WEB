namespace SoftTime.Infrastructure.Services.Closing;

public sealed record ResultatPeriode(
    IReadOnlyList<ClosingAnomaly> Anomalies,
    HashSet<(string Cle, DateTime Jour)> JoursControles,
    int SalariesSansPlanningExclus);
