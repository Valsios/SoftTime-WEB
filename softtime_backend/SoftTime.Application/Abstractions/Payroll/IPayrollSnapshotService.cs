namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Envoi en paie d'une période, avec son instantané s'il en a un (envois réussis depuis la migration 009).</summary>
public sealed record PayrollSendSummaryDto(
    int EnvoiId,
    string BddSage,
    DateTime PeriodeDebut,
    DateTime PeriodeFin,
    string TypeBase,
    DateTime EnvoyeLe,
    string? EnvoyePar,
    string Statut,
    int LignesEnvoi,
    int? InstantaneId,
    bool? AvecFiltre,
    string? Departement,
    string? MatriculeDe,
    string? MatriculeA,
    int? VersionFormat,
    string? Empreinte,
    int? NbLignes,
    int? NbSansLigne,
    int? NbNonRemis);

/// <summary>Ligne figée d'un instantané.</summary>
public sealed record PayrollSnapshotLineDto(
    string Matricule, int? NumSal, string Variable, string Destination, string Unite, decimal Valeur, bool RemiseAZero);

/// <summary>Instantané complet : entête et lignes, triées comme dans l'empreinte.</summary>
public sealed record PayrollSnapshotDetailDto(PayrollSendSummaryDto Envoi, IReadOnlyList<PayrollSnapshotLineDto> Lignes);

/// <summary>Résultat de la vérification d'un instantané : empreinte stockée et recalculée depuis les lignes relues.</summary>
public sealed record PayrollSnapshotVerificationDto(
    int InstantaneId,
    int VersionFormat,
    string EmpreinteStockee,
    string EmpreinteRecalculee,
    int NbLignesEntete,
    int NbLignesLues,
    bool Integre,
    string Message);

/// <summary>
/// Lecture et vérification des instantanés d'envoi en paie de la base active, en LECTURE SEULE : aucune méthode ne
/// modifie ni ne supprime un instantané. Mêmes droits que la lecture des variables envoyées (porte Bases + permission
/// SAGE:{base}:UPDATE, ou administrateur).
/// </summary>
public interface IPayrollSnapshotService
{
    /// <summary>Envois de la base active pour la période exacte, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<PayrollSendSummaryDto>> ListAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Instantané d'un envoi de la base active (404 s'il n'existe pas ou appartient à une autre base).</summary>
    Task<PayrollSnapshotDetailDto> GetAsync(int envoiId, CancellationToken ct = default);

    /// <summary>Relit l'instantané d'un envoi, recalcule son empreinte et signale toute altération.</summary>
    Task<PayrollSnapshotVerificationDto> VerifyAsync(int envoiId, CancellationToken ct = default);
}
