namespace SoftTime.Domain.Models;

/// <summary>Un jour ouvré rattaché à un salarié (matricule SAGE), sans notion d'heure.</summary>
public sealed record CanteenDay(string Matricule, DateTime Date);
