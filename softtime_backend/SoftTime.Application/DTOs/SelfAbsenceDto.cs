namespace SoftTime.Application.DTOs;

/// <summary>Absence ou congé constaté (Congé SAGE, Arbitrée GTA, Non justifiée…) avec sa durée planifiée en
/// heures.</summary>
public sealed record SelfAbsenceDto(DateTime Date, string Periode, string Type, decimal? DureeHeures, string? Motif);
