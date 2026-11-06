using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

/// <summary>Données chargées pour une analyse de clôture (période demandée et période précédente).</summary>
internal sealed record ClosingData(
    bool MultiPointage,
    IReadOnlyList<CardPaie> Cartes,
    IReadOnlyList<PlanningSalarie> Plannings,
    IReadOnlyList<Pointage> Pointages,
    IReadOnlyList<HeureCorrigee> Corrections,
    IReadOnlyList<HsValidee> SemainesValidees,
    IReadOnlySet<DateTime> Feries,
    IReadOnlyDictionary<string, List<LeaveDay>> Conges,
    DateTime DateDuJour)
{
    /// <summary>Pointages indexés une seule fois par (matricule, jour), partagés par toutes les règles.</summary>
    public ClosingPunchIndex IndexPointages { get; } = ClosingPunchIndex.Build(Pointages);
}
