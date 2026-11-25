namespace SoftTime.Application.Abstractions.Reports;

/// <summary>
/// Mesures du chargement d'un contexte de rapport (durées en millisecondes, lignes lues), pour les journaux de performance.
/// Aucune donnée personnelle : seulement des durées et des compteurs.
/// </summary>
/// <param name="FiltresPousses">Les filtres matricules / département ont réduit les lectures SQL des corrections et plannings.</param>
public sealed record ReportLoadStats(
    long CartesMs,
    int CartesLues,
    long FeriesMs,
    long CongesMs,
    long CorrectionsMs,
    int CorrectionsLues,
    long PlanningsMs,
    int PlanningsLues,
    long AutresMs,
    bool FiltresPousses)
{
    /// <summary>Lignes lues en base pour ce contexte (cartes, corrections et plannings).</summary>
    public int LignesLues => CartesLues + CorrectionsLues + PlanningsLues;
}
