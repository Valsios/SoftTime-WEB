namespace SoftTime.Application.DTOs;

/// <summary>Une anomalie du jour : <c>Nature</c> est un libellé (« Absence sans pointage », « Pointage à vérifier
/// »).</summary>
public sealed record SelfAnomalyDto(DateTime Date, string Nature, TimeSpan? Heure1, TimeSpan? Heure2, bool Ferie);
