namespace SoftTime.Domain.Models;

/// <summary>
/// Jour de congé développé : <see cref="Valeur"/> vaut <c>1</c> (journée entière) ou <c>0.5</c>
/// (demi-journée, aux bornes d'une période qui commence l'après-midi ou finit le matin).
/// </summary>
public sealed record LeaveDay(DateTime Date, decimal Valeur, string? Code);
