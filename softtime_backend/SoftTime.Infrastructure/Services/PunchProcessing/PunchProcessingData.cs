using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.PunchProcessing;

/// <summary>Données chargées une fois pour un pré-traitement de pointages sur une période.</summary>
internal sealed class PunchProcessingData
{
    public required string SageDb { get; init; }

    public required string PointeuseDb { get; init; }

    public required bool MultiPointage { get; init; }

    public required DateTime From { get; init; }

    public required DateTime To { get; init; }

    /// <summary>Cartes de paie du périmètre, une par matricule.</summary>
    public required List<CardPaie> Cards { get; init; }

    public required HashSet<DateTime> Feries { get; init; }

    /// <summary>Planning par identifiant de carte puis par jour.</summary>
    public required Dictionary<int, Dictionary<DateTime, PlanningSalarie>> PlanningParCarte { get; init; }

    /// <summary>Lundis des semaines validées, par clé de matricule.</summary>
    public required Dictionary<string, HashSet<DateTime>> SemainesValidees { get; init; }

    public required List<HeureCorrigee> CorrectionsExistantes { get; init; }

    public required List<HeureAnomalie> AnomaliesExistantes { get; init; }

    public required Dictionary<string, List<PunchMoment>> PointagesParCle { get; init; }
}
