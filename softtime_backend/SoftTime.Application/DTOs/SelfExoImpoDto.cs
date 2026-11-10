namespace SoftTime.Application.DTOs;

/// <summary>Cumul EXO / IMPO d'une période de calcul (exonéré et imposable, à 130 % et à 150 %).</summary>
public sealed record SelfExoImpoDto(
    DateTime PeriodeDebut,
    DateTime PeriodeFin,
    decimal Exo130,
    decimal Exo150,
    decimal Imp130,
    decimal Imp150);
