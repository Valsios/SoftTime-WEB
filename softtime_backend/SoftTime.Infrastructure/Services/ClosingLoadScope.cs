namespace SoftTime.Infrastructure.Services;

/// <summary>Périmètre d'un chargement de clôture : bases, période (comparaison incluse) et salariés retenus.</summary>
internal sealed record ClosingLoadScope(
    string SageDb, string PointeuseDb, DateTime Debut, DateTime Fin, HashSet<int> IdsCartes, HashSet<string> Cles);
