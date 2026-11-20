using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Payroll;

/// <summary>Lecture et vérification des instantanés (T_PAIE_INSTANTANE, T_PAIE_INSTANTANE_LIGNE). Aucune écriture.</summary>
public sealed class PayrollSnapshotService : IPayrollSnapshotService
{
    public const string Integre = "Instantané intègre : l'empreinte recalculée est identique à l'empreinte enregistrée.";
    public const string Altere = "Instantané ALTÉRÉ : l'empreinte recalculée diffère de l'empreinte enregistrée.";
    public const string SansInstantane = "Cet envoi n'a pas d'instantané (envoi en échec, incertain, ou antérieur à la migration 009).";

    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;

    public PayrollSnapshotService(IUnitOfWork uow, ITenantConnectionService tenant, IDatabaseConfigurationAuthorizer configAuth)
    {
        _uow = uow;
        _tenant = tenant;
        _configAuth = configAuth;
    }

    public async Task<IReadOnlyList<PayrollSendSummaryDto>> ListAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var baseSage = await AutoriserAsync(ct);
        return await EnvoisAsync(_uow, baseSage, from.Date, to.Date, ct);
    }

    /// <summary>Envois d'une base pour une période exacte, tous domaines, du plus récent au plus ancien, avec leur instantané.</summary>
    internal static async Task<IReadOnlyList<PayrollSendSummaryDto>> EnvoisAsync(
        IUnitOfWork uow, string baseSage, DateTime debut, DateTime fin, CancellationToken ct)
    {
        var envois = await uow.Repository<PaieEnvoi>().ListReadOnlyAsync(
            e => e.BddSage == baseSage && e.PeriodeDebut == debut && e.PeriodeFin == fin, ct);
        var instantanes = await uow.Repository<PaieInstantane>().ListReadOnlyAsync(
            i => i.BddSage == baseSage && i.PeriodeDebut == debut && i.PeriodeFin == fin, ct);
        return envois
            .OrderByDescending(e => e.EnvoyeLe).ThenByDescending(e => e.Id)
            .Select(e => Resume(e, instantanes.FirstOrDefault(i => i.EnvoiId == e.Id)))
            .ToList();
    }

    public async Task<PayrollSnapshotDetailDto> GetAsync(int envoiId, CancellationToken ct = default)
    {
        var baseSage = await AutoriserAsync(ct);
        var (envoi, instantane) = await LireAsync(baseSage, envoiId, ct);
        var lignes = await LignesAsync(_uow, instantane.Id, ct);
        return new PayrollSnapshotDetailDto(Resume(envoi, instantane), lignes
            .Select(l => new PayrollSnapshotLineDto(l.Matricule, l.NumSal, l.Variable, l.Destination, l.Unite, l.Valeur, l.RemiseAZero))
            .ToList());
    }

    public async Task<PayrollSnapshotVerificationDto> VerifyAsync(int envoiId, CancellationToken ct = default)
    {
        var baseSage = await AutoriserAsync(ct);
        var (_, instantane) = await LireAsync(baseSage, envoiId, ct);
        return Verifier(instantane, await LignesAsync(_uow, instantane.Id, ct));
    }

    /// <summary>
    /// Vérification pure : empreinte recalculée (dans la version enregistrée du format) depuis les lignes relues et l'entête,
    /// et nombre de lignes. Une version de format inconnue compte comme une altération.
    /// </summary>
    public static PayrollSnapshotVerificationDto Verifier(PaieInstantane instantane, IReadOnlyList<PayrollSnapshotLine> lignes)
    {
        string recalculee;
        try
        {
            // Le domaine choisit la première ligne du format (1 : HEURES ; 2 : CANTINE, FRAIS) ; un couple incohérent lève.
            recalculee = PayrollSnapshotFingerprint.Calculer(instantane.VersionFormat, instantane.Domaine, instantane.BddSage,
                instantane.PeriodeDebut, instantane.PeriodeFin, instantane.TypeBase, lignes);
        }
        catch (DomainException)
        {
            recalculee = string.Empty;
        }
        var stockee = (instantane.Empreinte ?? string.Empty).Trim();
        var integre = recalculee.Length > 0 && string.Equals(stockee, recalculee, StringComparison.Ordinal) && instantane.NbLignes == lignes.Count;
        return new PayrollSnapshotVerificationDto(instantane.Id, instantane.VersionFormat, stockee, recalculee, instantane.NbLignes,
            lignes.Count, integre, integre ? Integre : Altere);
    }

    /// <summary>Lignes d'un instantané, au format de l'empreinte, dans l'ordre canonique.</summary>
    internal static async Task<IReadOnlyList<PayrollSnapshotLine>> LignesAsync(IUnitOfWork uow, int instantaneId, CancellationToken ct) =>
        (await uow.Repository<PaieInstantaneLigne>().ListReadOnlyAsync(l => l.InstantaneId == instantaneId, ct))
            .Select(l => new PayrollSnapshotLine(l.Matricule, l.NumSal, l.Variable, l.Destination, l.Unite, l.Valeur, l.RemiseAZero))
            .OrderBy(l => l.Matricule, StringComparer.Ordinal)
            .ThenBy(l => l.Variable, StringComparer.Ordinal)
            .ThenBy(l => l.Destination, StringComparer.Ordinal)
            .ToList();

    private async Task<(PaieEnvoi Envoi, PaieInstantane Instantane)> LireAsync(string baseSage, int envoiId, CancellationToken ct)
    {
        var envoi = (await _uow.Repository<PaieEnvoi>().ListReadOnlyAsync(e => e.Id == envoiId && e.BddSage == baseSage, ct)).FirstOrDefault()
                    ?? throw new NotFoundException("Envoi en paie introuvable pour la base active.");
        var instantane = (await _uow.Repository<PaieInstantane>().ListReadOnlyAsync(i => i.EnvoiId == envoiId, ct)).FirstOrDefault()
                         ?? throw new NotFoundException(SansInstantane);
        return (envoi, instantane);
    }

    private async Task<string> AutoriserAsync(CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var database = await _tenant.GetSageRowAsync(ct);
        await _configAuth.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, database.NomBd, ct);
        if (string.IsNullOrWhiteSpace(database.NomBd))
            throw new DomainException("La base SAGE active n'a pas de nom.");
        return database.NomBd;
    }

    internal static PayrollSendSummaryDto Resume(PaieEnvoi e, PaieInstantane? i) => new(
        e.Id, e.BddSage, e.PeriodeDebut, e.PeriodeFin, e.TypeBase, e.EnvoyeLe, e.EnvoyePar, e.Statut, e.Lignes,
        i?.Id, i?.AvecFiltre, i?.Departement, i?.MatriculeDe, i?.MatriculeA, i?.VersionFormat, i?.Empreinte?.Trim(),
        i?.NbLignes, i?.NbSansLigne, i?.NbNonRemis, e.Domaine);
}
