#nullable enable

namespace SoftTime.Domain.Models;

/// <summary>Résultat d'une journée : heures retenues et compteurs de majoration.</summary>
public sealed record OvertimeDayResult(
    DateTime Jour,
    TimeSpan? Entree,
    TimeSpan? Sortie,
    decimal HeuresReelles,
    decimal HeuresRetenues,
    decimal Nuit,
    decimal Dimanche,
    decimal Ferie,
    decimal Retard,
    decimal Absence,
    decimal HeuresSup,
    bool Conge,
    bool JourTravaille,
    int NombrePaires);
