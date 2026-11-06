namespace SoftTime.Infrastructure.Services;

/// <summary>Période d'un calcul EXO/IMPO et bases du tenant.</summary>
internal sealed record ExoImpoPeriod(DateTime From, DateTime To, string SageDb, string PointeuseDb);
