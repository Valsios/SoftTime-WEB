namespace SoftTime.Application.DTOs;

/// <summary>Écart de comptage d'un type : <c>Ecart = Actuel − Precedent</c> (positif = plus d'anomalies).</summary>
public sealed record ClosingTypeDeltaDto(string Type, int Precedent, int Actuel, int Ecart);
