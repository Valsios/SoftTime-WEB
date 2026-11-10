using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Models;

/// <summary>Cartes, plannings et heures corrigées des salariés contrôlés.</summary>
public sealed record EmployeeSources(
    IReadOnlyList<CardPaie> Cartes,
    IReadOnlyList<PlanningSalarie> Plannings,
    IReadOnlyList<HeureCorrigee> Corrections);
