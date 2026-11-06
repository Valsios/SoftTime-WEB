namespace SoftTime.Infrastructure.Services;

/// <summary>Contexte d'un entraînement : société, période et volumétrie (positifs connus après sélection).</summary>
internal sealed record MlTrainingRun(string BddSage, DateTime From, DateTime To, int Rows, int? Positifs);
