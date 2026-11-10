namespace SoftTime.Infrastructure.Services.Overtime;

/// <summary>Salarié d'un calcul EXO/IMPO : matricule de la carte et numéro de salarié SAGE.</summary>
internal sealed record ExoImpoEmployee(string? Matricule, int? NumSal);
