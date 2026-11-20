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
        var envoi = Nouveau(send);
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

    /// <summary>Verrou applicatif partagé par l'écriture de l'envoi et de son instantané (transaction SoftTime unique).</summary>
    internal const string SnapshotLock = "SoftTime.PayrollSnapshot";

    public Task<int> RecordWithSnapshotAsync(PayrollSend send, PayrollSnapshotDraft snapshot, CancellationToken ct = default) => LoggedAsync(
        uow => uow.RunExclusiveAsync(SnapshotLock, async token =>
        {
            await RefuserSiClotureeAsync(uow, send.SageDatabase, send.PeriodFrom, send.PeriodTo, token);
            var envoi = Nouveau(send);
            await uow.Repository<PaieEnvoi>().AddAsync(envoi, token);
            await uow.SaveChangesAsync(token);
            await EcrireInstantaneAsync(uow, envoi, snapshot, token);
            return envoi.Id;
        }, ct),
        "Impossible d'enregistrer l'envoi en paie et son instantané pour la base {Base}.", send.SageDatabase);

    public Task CompleteWithSnapshotAsync(int id, string status, int lines, PayrollSnapshotDraft snapshot, CancellationToken ct = default) => LoggedAsync(
        uow => uow.RunExclusiveAsync(SnapshotLock, async token =>
        {
            var repo = uow.Repository<PaieEnvoi>();
            var envoi = await repo.GetByIdAsync(id, token) ?? throw new NotFoundException();
            await RefuserSiClotureeAsync(uow, envoi.BddSage, envoi.PeriodeDebut, envoi.PeriodeFin, token);
            envoi.Statut = status;
            envoi.Lignes = lines;
            repo.Update(envoi);
            await uow.SaveChangesAsync(token);
            await EcrireInstantaneAsync(uow, envoi, snapshot, token);
            return id;
        }, ct),
        "Impossible de terminer l'envoi en paie {Base} et d'écrire son instantané.", id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Sous le verrou partagé avec la clôture : un envoi commencé avant la clôture de sa période ne peut plus figer son
    /// instantané ni se terminer en SUCCES (AUTRE : la paie est annulée ; STANDARD : l'envoi devient INCERTAIN).
    /// </summary>
    private static async Task RefuserSiClotureeAsync(IUnitOfWork uow, string baseSage, DateTime from, DateTime to, CancellationToken ct)
    {
        var (debut, fin) = (from.Date, to.Date);
        var clotures = await uow.Repository<PaieCloture>().ListReadOnlyAsync(
            c => c.BddSage == baseSage && c.PeriodeDebut <= fin && c.PeriodeFin >= debut, ct);
        if (clotures.FirstOrDefault() is { } c)
            throw new ConflictException(new ClosedPeriod(c.PeriodeDebut, c.PeriodeFin).MessageEnvoi);
    }

    /// <summary>Entête (empreinte au format courant) puis lignes, dans la transaction de l'appelant.</summary>
    private static async Task EcrireInstantaneAsync(IUnitOfWork uow, PaieEnvoi envoi, PayrollSnapshotDraft snapshot, CancellationToken ct)
    {
        var lignes = snapshot.Lignes.Select(l => l.Normalisee()).ToList();
        var entete = new PaieInstantane
        {
            EnvoiId = envoi.Id,
            BddSage = envoi.BddSage,
            PeriodeDebut = envoi.PeriodeDebut,
            PeriodeFin = envoi.PeriodeFin,
            TypeBase = envoi.TypeBase,
            Domaine = envoi.Domaine,
            EnvoyeLe = envoi.EnvoyeLe,
            EnvoyePar = envoi.EnvoyePar,
            Departement = snapshot.Filtre.Departement,
            MatriculeDe = snapshot.Filtre.MatriculeDe,
            MatriculeA = snapshot.Filtre.MatriculeA,
            AvecFiltre = snapshot.Filtre.AvecFiltre,
            RemiseAZero = snapshot.RemiseAZero,
            HeuresParJour = snapshot.HeuresParJour,
            // Format 1 pour les heures (inchangé), format 2 pour la cantine et les frais.
            VersionFormat = PayrollDomains.VersionEmpreinte(envoi.Domaine),
            Empreinte = PayrollSnapshotFingerprint.CalculerDomaine(envoi.Domaine, envoi.BddSage, envoi.PeriodeDebut, envoi.PeriodeFin, envoi.TypeBase, lignes),
            NbLignes = lignes.Count,
            NbSansLigne = snapshot.SansLigne,
            NbNonRemis = snapshot.NonRemis,
            CreeLe = DateTime.UtcNow
        };
        await uow.Repository<PaieInstantane>().AddAsync(entete, ct);
        await uow.SaveChangesAsync(ct);
        await uow.Repository<PaieInstantaneLigne>().AddRangeAsync(lignes.Select(l => new PaieInstantaneLigne
        {
            InstantaneId = entete.Id,
            Matricule = l.Matricule,
            NumSal = l.NumSal,
            Variable = l.Variable,
            Destination = l.Destination,
            Unite = l.Unite,
            Valeur = l.Valeur,
            RemiseAZero = l.RemiseAZero
        }), ct);
        await uow.SaveChangesAsync(ct);
    }

    private static PaieEnvoi Nouveau(PayrollSend send) => new()
    {
        BddSage = send.SageDatabase,
        TypeBase = send.TypeBase,
        PeriodeDebut = send.PeriodFrom.Date,
        PeriodeFin = send.PeriodTo.Date,
        EnvoyeLe = DateTime.UtcNow,
        EnvoyePar = string.IsNullOrWhiteSpace(send.UserLogin) ? "SYSTEME" : send.UserLogin,
        Statut = send.Status,
        Lignes = send.Lines,
        Domaine = send.Domain
    };

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
        // Heures seulement : un envoi de cantine ou de frais ne verrouille pas la configuration des variables d'heures.
        var envois = await uow.Repository<PaieEnvoi>().ListReadOnlyAsync(e => e.BddSage == sageDb && e.Domaine == PayrollDomains.Heures, ct);
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
