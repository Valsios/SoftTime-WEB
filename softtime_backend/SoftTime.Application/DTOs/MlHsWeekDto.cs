namespace SoftTime.Application.DTOs;

/// <summary>Totaux HS prévus d'une semaine de l'horizon.</summary>
public sealed record MlHsWeekDto(
    DateTime SemaineDebut,
    int Salaries,
    // Salariés dont les HS prévues dépassent le plafond exonéré.
    int AuDelaPlafond,
    decimal HeuresPrevues,
    decimal BorneBasse,
    decimal BorneHaute);
