using Microsoft.Extensions.Logging;

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
    private const string TypeRetards = "retards";

    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IReportContextLoader _reportContextLoader;
    private readonly ILogger<RetardReportService>? _logger;

    public RetardReportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IReportContextLoader reportContextLoader,
        ILogger<RetardReportService>? logger = null)
    {
        _uow = uow;
        _tenant = tenant;
        _reportContextLoader = reportContextLoader;
        _logger = logger;
    }

    /// <summary>
    /// État retards : calcul complet (première entrée du jour vs planning + tolérance, comme le
    /// tableau de bord) puis écriture de <c>T_impr_RET</c> pour l'impression.
    /// </summary>
    public async Task<IReadOnlyList<RetardReportRow>> RetardsAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var chrono = new ReportChrono(_logger, TypeRetards, filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = await CalculerRetardsAsync(filter, chrono, ct);
        await chrono.EcrireAsync(() => WriteRetardsPrintAsync(rows, ct));
        chrono.Journaliser("liste", rows.Count);
        return rows;
    }

    /// <summary>État retards paginé, sans écriture de la table d'impression.</summary>
    public async Task<PagedResult<object>> RetardsPagedAsync(
        ReportFilter filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        var chrono = new ReportChrono(_logger, TypeRetards, filter);
        await _tenant.EnsureAuthorizedAsync(ct);
        var rows = await CalculerRetardsAsync(filter, chrono, ct);
        var resultat = chrono.Paginer(() => PagedLists.Page(rows.Cast<object>().ToList(), page, pageSize));
        chrono.Journaliser("pagine", rows.Count);
        return resultat;
    }

    /// <summary>Retards calculés de la période et du périmètre (mêmes règles que le dashboard).</summary>
    public async Task<IReadOnlyList<RetardReportRow>> CalculerRetardsAsync(ReportFilter filter, CancellationToken ct)
    {
        var chrono = new ReportChrono(_logger, TypeRetards, filter);
        var rows = await CalculerRetardsAsync(filter, chrono, ct);
        chrono.Journaliser("liste", rows.Count);
        return rows;
    }

    private async Task<IReadOnlyList<RetardReportRow>> CalculerRetardsAsync(ReportFilter filter, ReportChrono chrono, CancellationToken ct)
    {
        var ctx = await chrono.ChargerAsync(() => _reportContextLoader.ChargerContexteAsync(filter, avecTolerances: true, ct));
        if (ctx is null)
            return Array.Empty<RetardReportRow>();

        return chrono.Calculer(() => RetardReportRules.Calculer(
            new EmployeeSources(ctx.Cartes, ctx.Plannings, ctx.Corrections),
            new RetardTolerances(ctx.CategorieParCarte, ctx.Tolerances),
            ctx.Feries,
            new ReportPeriod(filter.From.Date, filter.To.Date, DateTime.Today)));
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
