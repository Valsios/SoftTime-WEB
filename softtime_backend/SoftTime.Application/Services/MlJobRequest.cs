namespace SoftTime.Application.Services;

/// <summary>Demande de traitement ML, exécutée par le service d'arrière-plan.</summary>
public sealed record MlJobRequest(string? BddSage, bool FullRefresh, bool Train);
