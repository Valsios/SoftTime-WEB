namespace SoftTime.Application.DTOs;

/// <summary>Jour détaillé du calcul des heures supplémentaires d'un salarié.</summary>
public record OvertimeDetailDayDto(
    DateTime Date,
    TimeSpan? Entree,
    TimeSpan? Sortie,
    decimal HeuresTravaillees,
    decimal HeuresSupJournalieres,
    decimal HeuresNuit,
    bool Dimanche,
    bool Ferie,
    TimeSpan? Retard,
    bool AbsenceAm,
    bool AbsencePm,
    bool Conge,
    bool JourTravaille,
    decimal HeuresReelles)
{
    public OvertimeDetailDayDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}
