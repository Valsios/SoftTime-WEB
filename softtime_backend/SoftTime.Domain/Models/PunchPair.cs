namespace SoftTime.Domain.Models;

/// <summary>Paire entrée/sortie retenue pour une journée.</summary>
public sealed record PunchPair(DateTime EntreeDate, TimeSpan EntreeHeure, DateTime SortieDate, TimeSpan SortieHeure);
