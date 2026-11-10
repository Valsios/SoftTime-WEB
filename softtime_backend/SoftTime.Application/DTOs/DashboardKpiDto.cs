namespace SoftTime.Application.DTOs;

/// <param name="JoursPlanifies">
/// Présence théorique de la période : jours de planning non off, hors week-end et férié.
/// </param>
/// <param name="TauxAbsenteisme">Jours d'absence / jours planifiés, en pourcentage.</param>
/// <param name="TauxPonctualite">
/// Jours travaillés sans retard / jours travaillés, en pourcentage.
/// </param>
public record DashboardKpiDto(
    int CartesActives,
    int Anomalies,
    int CorrectionsManuelles,
    int SemainesValidees,
    int SemainesEnAttente,
    decimal HeuresTravaillees,
    decimal HeuresSupplementaires,
    int JoursPlanifies,
    int JoursTravailles,
    int JoursAbsence,
    int JoursRetard,
    int MinutesRetard,
    decimal TauxAbsenteisme,
    decimal TauxPonctualite)
{
    public DashboardKpiDto() : this(default, default, default, default, default, default, default,
        default, default, default, default, default, default, default) { }
}
