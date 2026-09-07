using Microsoft.EntityFrameworkCore;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Infrastructure.Queries;

public class PunchAggregate : IPunchAggregate
{
    private readonly SoftTimeDbContext _db;

    public PunchAggregate(SoftTimeDbContext db) => _db = db;

    public async Task<IReadOnlyList<PunchCountDto>> CountCanteenDaysAsync(
        string sageDb,
        DateTime from,
        DateTime toExclusive,
        CancellationToken cancellationToken = default)
    {
        var start = new TimeSpan(11, 0, 0);
        var end = new TimeSpan(14, 30, 0);
        var rows = await _db.Database.SqlQuery<PunchCountRow>($"""
            SELECT MATRICULE_SAGE AS Matricule, COUNT(DISTINCT CAST(DATE_POINTAGE AS date)) AS Total
            FROM T_POINTAGE
            WHERE NOM_BDD_SAGE = {sageDb}
              AND DATE_POINTAGE >= {from}
              AND DATE_POINTAGE < {toExclusive}
              AND HEURE_POINTAGE >= {start}
              AND HEURE_POINTAGE <= {end}
              AND MATRICULE_SAGE IS NOT NULL
            GROUP BY MATRICULE_SAGE
            """).ToListAsync(cancellationToken);
        return rows.Select(r => new PunchCountDto(r.Matricule, r.Total)).ToList();
    }

    public async Task<IReadOnlyList<PunchCountDto>> CountTravelPunchesAsync(
        string sageDb,
        DateTime from,
        DateTime toExclusive,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Database.SqlQuery<PunchCountRow>($"""
            SELECT MATRICULE_SAGE AS Matricule, COUNT(*) AS Total
            FROM T_POINTAGE
            WHERE NOM_BDD_SAGE = {sageDb}
              AND DATE_POINTAGE >= {from}
              AND DATE_POINTAGE < {toExclusive}
              AND MATRICULE_SAGE IS NOT NULL
            GROUP BY MATRICULE_SAGE
            """).ToListAsync(cancellationToken);
        return rows.Select(r => new PunchCountDto(r.Matricule, r.Total)).ToList();
    }
}

internal sealed class PunchCountRow
{
    public string? Matricule { get; set; }
    public int Total { get; set; }
}
