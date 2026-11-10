namespace SoftTime.Domain.Models;

/// <summary>Période d'événement salarié lue dans SAGE (<c>T_GHRSAL</c>) : congé, absence, etc.</summary>
public sealed record LeavePeriod(DateTime Start, DateTime End, bool Matin, bool ApresMidi, string? Code);
