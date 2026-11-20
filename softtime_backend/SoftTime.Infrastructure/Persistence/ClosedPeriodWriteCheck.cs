using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>
/// Filet de sécurité de la clôture de paie (lot D3), appelé par <c>UnitOfWork.SaveChangesAsync</c> avant toute écriture :
/// si une ligne ajoutée, modifiée ou supprimée d'une table SOURCE datée (pointages, heures corrigées, anomalies, planning,
/// semaines, calculs HS, cantine, frais) a une date — avant OU après modification — dans une période clôturée de sa base,
/// l'enregistrement est refusé (409) et RIEN n'est écrit. Les services contrôlent déjà leurs entrées (message tôt) ; ce
/// filet garantit qu'aucun chemin oublié ne puisse modifier une période clôturée. Sans ligne source suivie, aucune requête ;
/// sinon une seule lecture indexée de T_PAIE_CLOTURE pour les bases concernées.
/// </summary>
internal static class ClosedPeriodWriteCheck
{
    /// <summary>Table source datée : base, date (ou début) et fin facultative (période ou semaine).</summary>
    private sealed record Source(string Base, string Debut, string? Fin);

    private static readonly Dictionary<Type, Source> Sources = new()
    {
        [typeof(Pointage)] = new(nameof(Pointage.NomBddSage), nameof(Pointage.DatePointage), null),
        [typeof(HeureCorrigee)] = new(nameof(HeureCorrigee.BddSage), nameof(HeureCorrigee.DatePointageIn), nameof(HeureCorrigee.DatePointageOut)),
        [typeof(HeureAnomalie)] = new(nameof(HeureAnomalie.BddSage), nameof(HeureAnomalie.DatePointageIn), nameof(HeureAnomalie.DatePointageOut)),
        [typeof(PlanningSalarie)] = new(nameof(PlanningSalarie.BddSage), nameof(PlanningSalarie.DateP), null),
        [typeof(HsValidee)] = new(nameof(HsValidee.BddSage), nameof(HsValidee.DateDeb), nameof(HsValidee.DateFin)),
        [typeof(HsSemaine)] = new(nameof(HsSemaine.BddSage), nameof(HsSemaine.PeriodeDebut), nameof(HsSemaine.PeriodeFin)),
        [typeof(HsExoImp)] = new(nameof(HsExoImp.BddSage), nameof(HsExoImp.PeriodeDebut), nameof(HsExoImp.PeriodeFin)),
        [typeof(Cantine)] = new(nameof(Cantine.NomBddSage), nameof(Cantine.DateDeb), nameof(Cantine.DateFin)),
        [typeof(Frais)] = new(nameof(Frais.NomBddSage), nameof(Frais.DateDeb), nameof(Frais.DateFin)),
        [typeof(HeureDimanche)] = new(nameof(HeureDimanche.BddSage), nameof(HeureDimanche.DateDim), null),
    };

    public static async Task EnsureAsync(SoftTimeDbContext db, CancellationToken ct)
    {
        var touchees = Touchees(db.ChangeTracker.Entries()).ToList();
        if (touchees.Count == 0)
            return;
        var bases = touchees.Select(t => t.Base).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var clotures = await db.Set<PaieCloture>().AsNoTracking()
            .Where(c => bases.Contains(c.BddSage))
            .Select(c => new { c.BddSage, c.PeriodeDebut, c.PeriodeFin })
            .ToListAsync(ct);
        if (clotures.Count == 0)
            return;
        foreach (var (baseSage, debut, fin) in touchees)
        {
            var cloture = clotures.FirstOrDefault(c => string.Equals(c.BddSage, baseSage, StringComparison.OrdinalIgnoreCase)
                                                       && debut.Date <= c.PeriodeFin.Date && fin.Date >= c.PeriodeDebut.Date);
            if (cloture is not null)
                throw new ConflictException(new ClosedPeriod(cloture.PeriodeDebut, cloture.PeriodeFin).MessageModification);
        }
    }

    /// <summary>Plages de dates (valeurs d'origine et actuelles) des lignes sources ajoutées, modifiées ou supprimées.</summary>
    private static IEnumerable<(string Base, DateTime Debut, DateTime Fin)> Touchees(IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || !Sources.TryGetValue(entry.Metadata.ClrType, out var source))
                continue;
            if (entry.State != EntityState.Added && Plage(entry, source, original: true) is { } avant)
                yield return avant;
            if (entry.State != EntityState.Deleted && Plage(entry, source, original: false) is { } apres)
                yield return apres;
        }
    }

    private static (string Base, DateTime Debut, DateTime Fin)? Plage(EntityEntry entry, Source source, bool original)
    {
        object? Lire(string propriete)
        {
            var p = entry.Property(propriete);
            return original ? p.OriginalValue : p.CurrentValue;
        }
        var baseSage = (Lire(source.Base) as string)?.Trim();
        if (string.IsNullOrEmpty(baseSage) || Lire(source.Debut) is not DateTime debut)
            return null;
        var fin = source.Fin is not null && Lire(source.Fin) is DateTime f && f >= debut ? f : debut;
        return (baseSage, debut, fin);
    }
}
