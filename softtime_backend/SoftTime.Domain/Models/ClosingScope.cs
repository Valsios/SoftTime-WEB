namespace SoftTime.Domain.Models;

/// <summary>Période contrôlée par une règle de clôture, mode de pointage et options (valeurs par défaut si absentes).</summary>
public sealed record ClosingScope(DateTime From, DateTime To, bool MultiPointage = false, ClosingRulesOptions? Options = null);
