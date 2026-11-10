namespace SoftTime.Domain.Models;

/// <summary>Gravité d'une anomalie de clôture : la valeur numérique est le poids par défaut du score.</summary>
public enum ClosingSeverity
{
    /// <summary>Constat sans effet sur la paie : poids 0 par défaut, ni score ni statut.</summary>
    Information = 0,
    Mineure = 1,
    Importante = 3,
    Critique = 5
}
