namespace SoftTime.Application.DTOs;

/// <summary>Synthèse de la période : comptages seulement.</summary>
public sealed record SelfSummaryDto(
    SelfPeriodDto Periode,
    int SemainesValidees,
    decimal HeuresSupplementaires,
    decimal Hs130,
    decimal Hs150,
    int Anomalies,
    int JoursAbsenceNonJustifiee,
    int JoursConge,
    int Retards,
    int MinutesRetardTotal);
