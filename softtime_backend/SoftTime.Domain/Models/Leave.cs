namespace SoftTime.Domain.Models;

/// <summary>Période d'événement salarié lue dans SAGE (<c>T_GHRSAL</c>) : congé, absence, etc.</summary>
public sealed record LeavePeriod(DateTime Start, DateTime End, bool Matin, bool ApresMidi, string? Code);

/// <summary>
/// Jour de congé développé : <see cref="Valeur"/> vaut <c>1</c> (journée entière) ou <c>0.5</c>
/// (demi-journée, aux bornes d'une période qui commence l'après-midi ou finit le matin).
/// </summary>
public sealed record LeaveDay(DateTime Date, decimal Valeur, string? Code);
