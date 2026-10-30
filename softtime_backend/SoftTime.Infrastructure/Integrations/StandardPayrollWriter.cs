using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>Adaptateur qui préserve intégralement l'écriture historique T_CUMSAL.</summary>
public sealed class StandardPayrollWriter : IPayrollWriter
{
    private readonly ISagePayrollWriter _sageWriter;
    private readonly ISageContextFactory _sageFactory;
    private readonly TenantConnectionService _tenant;
    private readonly CatalogService _catalog;

    public StandardPayrollWriter(ISagePayrollWriter sageWriter, ISageContextFactory sageFactory, TenantConnectionService tenant, CatalogService catalog)
    {
        _sageWriter = sageWriter;
        _sageFactory = sageFactory;
        _tenant = tenant;
        _catalog = catalog;
    }

    public bool CanHandle(T_BDD_SAGE database) => string.Equals(database.TYPE_BASE, "STANDARD", StringComparison.OrdinalIgnoreCase);

    public async Task<PayrollWriteResultDto> ValidateAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
    {
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var constants = sage.Constants.ToArray();
        var codes = await _catalog.GetOvertimeSageCodesAsync(ct);
        foreach (var code in new[] { codes.Exo130, codes.Exo150, codes.I130, codes.I150, codes.Ferie, codes.Dim, codes.Nuit })
            if (!constants.Any(c => string.Equals(c.CodeConstante?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Constante SAGE {code} introuvable.");
        return new PayrollWriteResultDto(database.NOM_BD ?? string.Empty, "STANDARD", 0, 0, "Configuration SAGE valide.");
    }

    public async Task<PayrollWriteResultDto> WriteAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
    {
        // Le flux ci-dessous est intentionnellement celui qui existait dans OvertimeService.
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var constants = sage.Constants.ToArray();
        var connection = await _tenant.GetSageConnectionAsync(ct);
        var codes = await _catalog.GetOvertimeSageCodesAsync(ct);
        var written = 0;
        var sansSalarie = new List<string>();
        var sansCumul = new List<string>();
        foreach (var hs in rows)
        {
            if (!hs.NumSal.HasValue)
            {
                sansSalarie.Add(hs.Matricule ?? "?");
                continue;
            }
            var ecrit = await _sageWriter.SyncOvertimeAsync(connection, constants, hs.NumSal.Value, codes,
                hs.Exo130 ?? 0, hs.Exo150 ?? 0, hs.I130 ?? 0, hs.I150 ?? 0,
                hs.Ferie ?? 0, hs.Nuit ?? 0, hs.Dim ?? 0, ct);
            if (ecrit)
                written++;
            else
                sansCumul.Add(hs.Matricule ?? hs.NumSal.Value.ToString());
        }

        // Bilan réel : un salarié non écrit est nommé, au lieu d'être compté comme synchronisé.
        var message = $"Synchronisation des heures supplémentaires vers SAGE : {written} salarié(s) mis à jour.";
        if (sansSalarie.Count > 0)
            message += $" {sansSalarie.Count} matricule(s) absent(s) de SAGE : {Liste(sansSalarie)}.";
        if (sansCumul.Count > 0)
            message += $" {sansCumul.Count} salarié(s) sans ligne de cumul HS dans T_CUMSAL (rien n'a été écrit pour eux) : {Liste(sansCumul)}.";
        return new PayrollWriteResultDto(database.NOM_BD ?? string.Empty, "STANDARD", written,
            written * PayrollWritingConfiguration.Categories.Length, message);
    }

    private static string Liste(IReadOnlyList<string> matricules)
        => string.Join(", ", matricules.Take(10)) + (matricules.Count > 10 ? "…" : string.Empty);
}
