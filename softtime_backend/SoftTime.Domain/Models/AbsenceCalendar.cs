namespace SoftTime.Domain.Models;

/// <summary>Jours non travaillés : congés SAGE par clé matricule et jours fériés.</summary>
public sealed record AbsenceCalendar(IReadOnlyDictionary<string, List<LeaveDay>> CongesParCle, IReadOnlySet<DateTime> Feries);
