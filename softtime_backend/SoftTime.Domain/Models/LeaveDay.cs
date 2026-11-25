namespace SoftTime.Domain.Models;

/// <summary>
/// Jour de congé développé : <see cref="Valeur"/> vaut <c>1</c> (journée entière) ou <c>0.5</c>
/// (demi-journée, aux bornes d'une période qui commence l'après-midi ou finit le matin).
/// <see cref="Libelle"/> : intitulé de l'événement SAGE, facultatif. <see cref="Moitie"/> : moitié de la journée
/// couverte quand la valeur est une demi-journée et que la période l'indique (inconnue sinon).
/// </summary>
public sealed record LeaveDay(DateTime Date, decimal Valeur, string? Code, string? Libelle = null, LeaveHalf? Moitie = null);

/// <summary>Moitié de journée couverte par un congé d'une demi-journée.</summary>
public enum LeaveHalf
{
    Matin = 0,
    ApresMidi = 1
}
