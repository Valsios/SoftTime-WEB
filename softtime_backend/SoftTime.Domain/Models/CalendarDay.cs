namespace SoftTime.Domain.Models;

/// <summary>Journée du calendrier d'un salarié : date, jour férié et part de congé SAGE (0 à 1).</summary>
public sealed record CalendarDay(DateTime Jour, bool Ferie, decimal CongeValeur);
