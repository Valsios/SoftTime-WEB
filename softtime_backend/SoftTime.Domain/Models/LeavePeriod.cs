namespace SoftTime.Domain.Models;

/// <summary>
/// Période d'événement salarié lue dans SAGE (<c>T_GHRSAL</c>) : congé, absence, etc.
/// <see cref="Libelle"/> : intitulé de l'événement dans le catalogue SAGE (<c>T_GHR</c>), facultatif.
/// </summary>
public sealed record LeavePeriod(DateTime Start, DateTime End, bool Matin, bool ApresMidi, string? Code, string? Libelle = null);
