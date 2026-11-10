namespace SoftTime.Application.Abstractions.Ml;

/// <summary>Société et plage de dates (bornes incluses) d'une écriture du stockage ML.</summary>
public sealed record MlPeriod(string BddSage, DateTime From, DateTime To);
