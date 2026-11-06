using Microsoft.EntityFrameworkCore;

using SoftTime.Application.Abstractions;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Infrastructure.Queries;

/// <summary>
/// Agrégat SQL des jours planifiés par carte salarié. Une seule requête <c>GROUP BY</c> par
/// affichage, au lieu de charger toutes les lignes de <c>T_PLANNING_SAL</c> puis de compter
/// en mémoire.
/// </summary>
public class PlanningStatisticsReader : IPlanningStatisticsReader
{
    private readonly SoftTimeDbContext _db;

    public PlanningStatisticsReader(SoftTimeDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlanningDayCount>> CountPlannedDaysAsync(
        string sageDb,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var start = from.Date;
        var end = to.Date.AddDays(1);
        var rows = await _db.PlanningSalaries
            .AsNoTracking()
            .Where(p => (p.BddSage == null || p.BddSage.Trim() == "" || p.BddSage.Trim() == sageDb)
                        && p.DateP >= start
                        && p.DateP < end)
            .GroupBy(p => p.IdSal)
            .Select(g => new { CardPaieId = g.Key, PlannedDays = g.Count() })
            .ToListAsync(ct);
        return rows
            .Select(r => new PlanningDayCount(r.CardPaieId, r.PlannedDays))
            .ToList();
    }
}
