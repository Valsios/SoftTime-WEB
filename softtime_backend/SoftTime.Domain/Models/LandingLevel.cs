namespace SoftTime.Domain.Models;

/// <summary>Seuil hebdomadaire d'heures supplémentaires que l'atterrissage d'un salarié franchit.</summary>
public enum LandingLevel
{
    /// <summary>Aucune heure supplémentaire attendue.</summary>
    Aucun = 0,

    /// <summary>Des heures supplémentaires sont attendues (majoration à 130 %).</summary>
    HeuresSup = 1,

    /// <summary>Au-delà de <c>OvertimeRules.WeeklyHs130Cap</c> : passage à 150 %.</summary>
    Majoration150 = 2,

    /// <summary>Au-delà de <c>OvertimeRules.ExemptCeilingHours</c> : heures imposables.</summary>
    Plafond = 3
}
