namespace SoftTime.Domain.Models;

/// <summary>
/// Types d'anomalie contrôlés avant la clôture de paie. Les deux derniers (corrections manuelles massives, heures
/// supplémentaires atypiques) sont des signaux de vigilance, de niveau « Information » par défaut : sans effet sur le
/// score ni le statut tant qu'une gravité n'est pas réglée. Ajouts en FIN d'énumération : l'ordre de tri des types
/// existants ne change pas. Le croisement congé SAGE / pointage relève d'une phase ultérieure.
/// </summary>
public enum ClosingAnomalyType
{
    PointageIncomplet,
    PointageManquant,
    PlanningSansPointage,
    PointageSansPlanning,
    DoublonExact,
    QuasiDoublon,
    FerieNonApplique,
    CorrectionsManuelles,
    HeuresSupAtypiques
}
