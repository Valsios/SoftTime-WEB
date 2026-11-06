namespace SoftTime.Domain.Models;

/// <summary>
/// Anomalie cantine : le total du jour est contradictoire entre les deux sources et doit être revu.
/// </summary>
public sealed record CanteenAnomaly(string Matricule, DateTime Date, string Motif);
