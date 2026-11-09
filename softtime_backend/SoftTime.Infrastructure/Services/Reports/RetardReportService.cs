using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Reports;

public sealed class RetardReportService : IRetardReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IReportContextLoader _reportContextLoader;

    public RetardReportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IReportContextLoader reportContextLoader)
    {
        _uow = uow;
        _tenant = tenant;
        _reportContextLoader = reportContextLoader;
    }

    /// <summary>
    /// État retards : calcul complet (première entrée du jour vs planning + tolérance, comme le
    /// tableau de bord) puis écriture de <c>T_impr_RET</c> pour l'impression.
    /// </summary>
    public async Task<IReadOnlyList<RetardReportRow>> RetardsAsync(ReportFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = await CalculerRetardsAsync(filter, ct);
        await WriteRetardsPrintAsync(rows, ct);
        return rows;
    }

    /// <summary>État retards paginé, sans écriture de la table d'impression.</summary>
    public async Task<PagedResult<object>> RetardsPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = await CalculerRetardsAsync(filter, ct);
        return PagedLists.Page(rows.Cast<object>().ToList(), page, pageSize);
    }

    /// <summary>Retards calculés de la période et du périmètre (mêmes règles que le dashboard).</summary>
    public async Task<IReadOnlyList<RetardReportRow>> CalculerRetardsAsync(ReportFilter filter, CancellationToken ct)
    {
        var ctx = await _reportContextLoader.ChargerContexteAsync(filter, ct);
        if (ctx is null)
            return Array.Empty<RetardReportRow>();

        return RetardReportRules.Calculer(
            new EmployeeSources(ctx.Cartes, ctx.Plannings, ctx.Corrections),
            new RetardTolerances(ctx.CategorieParCarte, ctx.Tolerances),
            ctx.Feries,
            new ReportPeriod(filter.From.Date, filter.To.Date, DateTime.Today));
    }

    /// <summary>
    /// Recopie les retards calculés dans la table tampon d'impression <c>T_impr_RET</c>
    /// (équivalent du bouton « Générer état » du desktop). La table est purgée avant réinsertion.
    /// </summary>
    private async Task WriteRetardsPrintAsync(IReadOnlyList<RetardReportRow> rows, CancellationToken ct)
    {
        var print = _uow.Repository<ImpressionRetard>();
        var old = await print.ListAsync(_ => true, ct);
        print.RemoveRange(old);
        foreach (var row in rows)
        {
            await print.AddAsync(new ImpressionRetard
            {
                BddSage = _tenant.SageDb,
                BddPointeuse = _tenant.PointeuseDb,
                Matricules = row.Matricule,
                DatePointage = row.Date.ToString("dd/MM/yyyy"),
                HeureEntree = row.HeureEntree,
                Retards = TimeSpan.FromMinutes(row.MinutesRetard)
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }
}
