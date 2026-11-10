namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>Comptages lus pour la checklist : des volumes seulement, jamais de matricule ni de nom.</summary>
internal sealed record ClosingChecklistData(
    int Pointages,
    int SalariesPointes,
    int Anomalies,
    int Corrections,
    int SemainesAttendues,
    int SemainesValidees,
    int ExoExactes,
    int ExoRecoupantes,
    int PeriodesExoDistinctes,
    int SalariesImposables,
    string? DernierResumeSynchro);
