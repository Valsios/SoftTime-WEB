namespace SoftTime.Infrastructure.Services;

/// <summary>Ligne de validation hebdomadaire ramenée au matricule de la carte.</summary>
public sealed record SemaineHs(string Cle, DateTime DateDeb, decimal Ht, decimal Hs, bool Validee);
