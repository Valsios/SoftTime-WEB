using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Payroll;

/// <summary>
/// T_PAIE_ENVOI. Chaque écriture passe par une portée dédiée : elle est sauvegardée tout de suite, sans emporter
/// d'autres modifications en attente de la requête, et indépendamment du réglage du journal central.
/// </summary>
public sealed class PayrollSendLog : IPayrollSendLog
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PayrollSendLog> _logger;

    public PayrollSendLog(IServiceScopeFactory scopeFactory, ILogger<PayrollSendLog> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<int> RecordAsync(PayrollSend send, CancellationToken ct = default) => LoggedAsync(async uow =>
    {
        var envoi = new PaieEnvoi
        {
            BddSage = send.SageDatabase,
            TypeBase = send.TypeBase,
            PeriodeDebut = send.PeriodFrom.Date,
            PeriodeFin = send.PeriodTo.Date,
            EnvoyeLe = DateTime.UtcNow,
            EnvoyePar = string.IsNullOrWhiteSpace(send.UserLogin) ? "SYSTEME" : send.UserLogin,
            Statut = send.Status,
            Lignes = send.Lines
        };
        await uow.Repository<PaieEnvoi>().AddAsync(envoi, ct);
        await uow.SaveChangesAsync(ct);
        return envoi.Id;
    }, "Impossible d'enregistrer l'envoi en paie de la base {Base}.", send.SageDatabase);

    public Task CompleteAsync(int id, string status, int lines, CancellationToken ct = default) => LoggedAsync(async uow =>
    {
        var repo = uow.Repository<PaieEnvoi>();
        var envoi = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        envoi.Statut = status;
        envoi.Lignes = lines;
        repo.Update(envoi);
        await uow.SaveChangesAsync(ct);
        return id;
    }, "Impossible de mettre à jour l'envoi en paie {Base}.", id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public Task RemoveAsync(int id, CancellationToken ct = default) => LoggedAsync(async uow =>
    {
        var repo = uow.Repository<PaieEnvoi>();
        if (await repo.GetByIdAsync(id, ct) is { } envoi)
        {
            repo.Remove(envoi);
            await uow.SaveChangesAsync(ct);
        }
        return id;
    }, "Impossible de retirer l'envoi en paie {Base}.", id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public async Task<IReadOnlyList<(DateTime Debut, DateTime Fin)>> ListPeriodsAsync(string sageDb, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var envois = await uow.Repository<PaieEnvoi>().ListReadOnlyAsync(e => e.BddSage == sageDb, ct);
        return envois.Select(e => (e.PeriodeDebut, e.PeriodeFin)).ToList();
    }

    private async Task<int> LoggedAsync(Func<IUnitOfWork, Task<int>> action, string message, string sujet)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, message, sujet);
            throw;
        }
    }
}
