namespace SoftTime.Domain.Models;

/// <summary>
/// Types d'anomalie contrôlés avant la clôture de paie. Les heures supplémentaires inhabituelles
/// et le croisement congé SAGE / pointage relèvent d'une phase ultérieure.
/// </summary>
public enum ClosingAnomalyType
{
    PointageIncomplet,
    PointageManquant,
    PlanningSansPointage,
    PointageSansPlanning,
    DoublonExact,
    QuasiDoublon,
    FerieNonApplique
}
