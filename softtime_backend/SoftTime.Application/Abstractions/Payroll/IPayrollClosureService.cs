using System.Globalization;

namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Période clôturée d'une base (bornes incluses).</summary>
public sealed record ClosedPeriod(DateTime Debut, DateTime Fin)
{
    public bool Contient(DateTime date) => date.Date >= Debut.Date && date.Date <= Fin.Date;

    public bool Recoupe(DateTime from, DateTime to) => from.Date <= Fin.Date && to.Date >= Debut.Date;

    public string Libelle => string.Create(CultureInfo.InvariantCulture, $"du {Debut:dd/MM/yyyy} au {Fin:dd/MM/yyyy}");

    /// <summary>Refus commun d'une modification de donnée qui tombe dans cette période (409).</summary>
    public string MessageModification =>
        $"Période clôturée ({Libelle}) : modification impossible. Les corrections se font sur la période suivante.";

    /// <summary>Refus d'un nouvel envoi en paie d'une période qui recoupe cette période (409).</summary>
    public string MessageEnvoi =>
        $"Période clôturée ({Libelle}) : nouvel envoi en paie impossible. Les corrections se font sur la période suivante.";
}

/// <summary>
/// Contrôle commun « période clôturée » de la base active, réutilisé par tous les points de modification (envoi, pointages,
/// heures corrigées, anomalies, planning, validation, calcul, purge, cantine, frais…). Il ne fait que LIRE : un refus lève
/// une <c>ConflictException</c> (409) avant toute écriture. Sans aucune clôture, il ne refuse jamais rien.
/// </summary>
public interface IPayrollClosureGuard
{
    /// <summary>Périodes clôturées de la base active (lues une fois par requête).</summary>
    Task<IReadOnlyList<ClosedPeriod>> ClosedPeriodsAsync(CancellationToken ct = default);

    /// <summary>Première période clôturée qui recoupe [from, to], ou null.</summary>
    Task<ClosedPeriod?> FindOverlapAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Refuse (409) si [from, to] recoupe une période clôturée de la base active.</summary>
    Task EnsureOpenAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Refuse (409) si l'une des dates tombe dans une période clôturée de la base active.</summary>
    Task EnsureOpenAsync(IEnumerable<DateTime> dates, CancellationToken ct = default);
}

/// <summary>Demande de clôture : période exacte d'un envoi (bornes incluses).</summary>
public sealed record PayrollClosureRequest(DateTime From, DateTime To);

/// <summary>Clôture enregistrée.</summary>
public sealed record PayrollClosureDto(
    int Id,
    string BddSage,
    DateTime PeriodeDebut,
    DateTime PeriodeFin,
    DateTime ClotureLe,
    string CloturePar,
    int EnvoiId,
    int InstantaneId,
    string Empreinte,
    string StatutControle,
    string ResumeControle);

/// <summary>État d'une période envoyée de la base active : « ENVOYEE » ou « CLOTUREE ».</summary>
public sealed record PayrollPeriodStateDto(
    DateTime PeriodeDebut,
    DateTime PeriodeFin,
    string Etat,
    PayrollSendSummaryDto DernierEnvoi,
    PayrollClosureDto? Cloture);

/// <summary>Périodes envoyées de la base active et droit de clôturer de l'appelant.</summary>
public sealed record PayrollPeriodsDto(string BddSage, bool PeutCloturer, IReadOnlyList<PayrollPeriodStateDto> Periodes);

/// <summary>Périodes clôturées qui recoupent une sélection (bandeau « Période clôturée » des pages de modification).</summary>
public sealed record PayrollClosureStateDto(bool Cloturee, IReadOnlyList<ClosedPeriod> Periodes, string? Message);

/// <summary>
/// Clôture de paie de la base active : action unique, définitive, SANS déblocage (aucune méthode d'annulation ni de
/// suppression). Exige le droit SAGE:{base}:CLOTURE (porte Bases) ou l'administrateur.
/// </summary>
public interface IPayrollClosureService
{
    /// <summary>
    /// Clôture la période exacte [from, to] : période terminée ; dernier envoi réussi (SUCCES, non INCERTAIN), sans filtre,
    /// avec instantané intègre et concordant avec les heures actuelles ; Contrôle avant paie « Prêt » sur la base entière
    /// (sans département, sans cache) ; aucune autre clôture qui recoupe la période. Journalisée.
    /// </summary>
    Task<PayrollClosureDto> CloseAsync(PayrollClosureRequest request, CancellationToken ct = default);

    /// <summary>Périodes envoyées de la base active avec leur état (envoyée, clôturée).</summary>
    Task<PayrollPeriodsDto> ListPeriodsAsync(CancellationToken ct = default);

    /// <summary>Périodes clôturées qui recoupent [from, to] (lecture pour les bandeaux, tout utilisateur de la base).</summary>
    Task<PayrollClosureStateDto> StateAsync(DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>Appels du contrôle commun depuis les services (garde facultative : absente dans certains doubles de test).</summary>
public static class ClosureChecks
{
    /// <summary>Refuse (409) si [from, to] recoupe une période clôturée de la base active.</summary>
    public static Task OuverteAsync(IPayrollClosureGuard? guard, DateTime from, DateTime to, CancellationToken ct)
        => guard is null ? Task.CompletedTask : guard.EnsureOpenAsync(from, to, ct);

    /// <summary>Refuse (409) si l'une des dates renseignées tombe dans une période clôturée de la base active.</summary>
    public static Task OuverteAsync(IPayrollClosureGuard? guard, IEnumerable<DateTime?> dates, CancellationToken ct)
        => guard is null ? Task.CompletedTask : guard.EnsureOpenAsync(dates.Where(d => d.HasValue).Select(d => d!.Value).ToList(), ct);
}
