namespace SoftTime.Domain.Models;

/// <summary>Un jour ouvré rattaché à un salarié (matricule SAGE), sans notion d'heure.</summary>
public sealed record CanteenDay(string Matricule, DateTime Date);

/// <summary>
/// Anomalie cantine : le total du jour est contradictoire entre les deux sources et doit être revu.
/// </summary>
public sealed record CanteenAnomaly(string Matricule, DateTime Date, string Motif);
