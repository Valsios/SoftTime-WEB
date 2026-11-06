using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class ValidatedWeekGuard : IValidatedWeekGuard
{
    private readonly IUnitOfWork _uow;

    public ValidatedWeekGuard(
        IUnitOfWork uow)
    {
        _uow = uow;
    }

    /// <summary>Refuse toute modification rétroactive sur une semaine déjà validée.</summary>
    public async Task RefuserSiSemaineValideeAsync(string sageDb, string? matricule, DateTime? jour, CancellationToken ct)
    {
        if (matricule is null || jour is null)
            return;
        var lundi = jour.Value.Date.AddDays(-(((int)jour.Value.DayOfWeek + 6) % 7));
        var semaines = await _uow.Repository<HsValidee>().ListAsync(
            v => v.BddSage == sageDb && v.DateDeb == lundi && v.ValidationHs == true, ct);
        if (semaines.Any(v => MemeIdentifiant(v.Matricules, matricule)))
            throw new DomainException(
                $"La semaine du {lundi:dd/MM/yyyy} est validée : annulez la validation avant de modifier les pointages.");
    }

    /// <summary>
    /// Semaines validées de la base active, par (clé matricule, lundi) : chargées une fois pour les
    /// opérations de masse (import, validation d'anomalies) au lieu d'une requête par ligne.
    /// </summary>
    public async Task<HashSet<(string Cle, DateTime Lundi)>> ChargerSemainesValideesAsync(string sageDb, CancellationToken ct)
        => (await _uow.Repository<HsValidee>().ListAsync(
                v => v.BddSage == sageDb && v.ValidationHs == true && v.DateDeb != null, ct))
            .Where(v => !string.IsNullOrWhiteSpace(v.Matricules))
            .Select(v => (MatriculeIdentity.Cle(v.Matricules), v.DateDeb!.Value.Date))
            .ToHashSet();

    /// <summary>Comparaison de matricule insensible au remplissage par des zéros (« 00369 » == « 369 »).</summary>
    private static bool MemeIdentifiant(string? a, string? b)
    {
        a = a?.Trim();
        b = b?.Trim();
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        if (long.TryParse(a, out var na) && long.TryParse(b, out var nb))
            return na == nb;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
