using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class HistoryArchiveService : IHistoryArchiveService
{
    private readonly IUnitOfWork _uow;

    public HistoryArchiveService(
        IUnitOfWork uow)
    {
        _uow = uow;
    }

    // ---------------------------------------------------------------- Archivage (interne)

    public Task ArchivePunchesAsync(IReadOnlyCollection<Pointage> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(p => new HistoriquePointage
        {
            PointageId = p.IdPointage,
            NomBddSage = p.NomBddSage,
            NomBddPointeuse = p.NomBddPointeuse,
            MatriculeSage = p.MatriculeSage,
            DatePointage = p.DatePointage,
            HeurePointage = p.HeurePointage,
            DateImportation = p.DateImportation,
            IdUniquePointage = p.IdUniquePointage,
            TypePointage = p.TypePointage,
            DateSuppression = now
        }).ToList();
        return _uow.Repository<HistoriquePointage>().AddRangeAsync(archives, ct);
    }

    public Task ArchiveAnomaliesAsync(IReadOnlyCollection<HeureAnomalie> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(a => new HistoriqueAnomalie
        {
            AnomalieId = a.Id,
            BddSage = a.BddSage,
            BddPointeuse = a.BddPointeuse,
            MatriculeSage = a.MatriculeSage,
            DatePointageIn = a.DatePointageIn,
            DatePointageOut = a.DatePointageOut,
            H1 = a.H1,
            H2 = a.H2,
            HEntree = a.HEntree,
            Hps = a.Hps,
            Hpe = a.Hpe,
            HSortie = a.HSortie,
            AbsAm = a.AbsAm,
            AbsPm = a.AbsPm,
            Feries = a.Feries,
            FeriesAm = a.FeriesAm,
            FeriesPm = a.FeriesPm,
            IntituleAbsence = a.IntituleAbsence,
            DateSuppression = now
        }).ToList();
        return _uow.Repository<HistoriqueAnomalie>().AddRangeAsync(archives, ct);
    }

    public Task ArchiveHsAsync(IReadOnlyCollection<HsExoImp> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;
        var now = DateTime.Now;
        var archives = rows.Select(h => new HistoriqueHs
        {
            HsId = h.Oid,
            Matricule = h.Matricule,
            NumSal = h.NumSal,
            Exo130 = h.Exo130,
            Exo150 = h.Exo150,
            I130 = h.I130,
            I150 = h.I150,
            PeriodeDebut = h.PeriodeDebut,
            PeriodeFin = h.PeriodeFin,
            Ferie = h.Ferie,
            Nuit = h.Nuit,
            Dim = h.Dim,
            Retard = h.Retard,
            Absence = h.Absence,
            BddSage = h.BddSage,
            BddPointeuse = h.BddPointeuse,
            DateSuppression = now
        }).ToList();
        return _uow.Repository<HistoriqueHs>().AddRangeAsync(archives, ct);
    }
}
