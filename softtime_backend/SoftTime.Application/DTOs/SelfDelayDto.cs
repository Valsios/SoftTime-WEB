namespace SoftTime.Application.DTOs;

/// <summary>Un retard du jour : entrée retenue, heure prévue, tolérance de la catégorie et retard en minutes.</summary>
public sealed record SelfDelayDto(
    DateTime Date,
    TimeSpan? HeureEntree,
    TimeSpan? HeurePrevue,
    int ToleranceMinutes,
    int MinutesRetard,
    bool RetardSaisi);
