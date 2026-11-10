using System.Linq.Expressions;

using SoftTime.Application.Abstractions.EmployeePlanning;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.EmployeePlanning;

public sealed class ShiftService : IShiftService
{
    private readonly IUnitOfWork _uow;

    public ShiftService(
        IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<Planning>().ListReadOnlyAsync(_ => true, ct);
        return list.Select(ToShiftDto).ToList();
    }

    /// <summary>
    /// Liste paginée du catalogue des shifts. Le catalogue reste petit, mais l'endpoint suit le
    /// même contrat que les autres listes (page bornée, total serveur) pour éviter un découpage
    /// côté client.
    /// </summary>
    public async Task<PagedResult<ShiftDto>> ListShiftsPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
        => await PagedLists.From(_uow.Repository<Planning>(), _ => true, q => q.OrderBy(s => s.NoShift)
                  .ThenBy(s => s.Intitule)
                  .ThenBy(s => s.IdPlanning)).WithSearch(search, new Expression<Func<Planning, string?>>[] { s => s.Intitule }).PageAsync(new PageRequest(page, pageSize), ToShiftDto, ct);

    private static ShiftDto ToShiftDto(Planning s) => new(
        s.IdPlanning, s.NoShift, s.Ha, s.Pause, s.Hd, s.Intitule);

    public async Task<ShiftDto> SaveShiftAsync(ShiftDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Planning>();
        Planning entity;
        if (dto.Id == 0)
        {
            entity = new Planning { NoShift = dto.NoShift, Ha = dto.Ha, Pause = dto.Pause, Hd = dto.Hd, Intitule = dto.Intitule };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            entity.NoShift = dto.NoShift;
            entity.Ha = dto.Ha;
            entity.Pause = dto.Pause;
            entity.Hd = dto.Hd;
            entity.Intitule = dto.Intitule;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new ShiftDto(entity.IdPlanning, entity.NoShift, entity.Ha, entity.Pause, entity.Hd, entity.Intitule);
    }

    public async Task DeleteShiftAsync(decimal id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Planning>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportResultDto> ImportShiftsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Planning>();
        var existing = await repo.ListAsync(_ => true, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var noRaw = ExcelCells.Get(row, "NoSHIFT", "N° SHIFT", "No SHIFT", "SHIFT", "N°");
            if (!int.TryParse(noRaw, out var noShift))
            {
                skipped++;
                continue;
            }
            await EnregistrerShiftAsync(repo, existing, row, noShift, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped,
            $"{imported} shift(s) importé(s), {skipped} ligne(s) ignorée(s).");
    }

    /// <summary>Crée le shift ou complète l'existant avec les heures et l'intitulé renseignés dans la ligne.</summary>
    private static async Task EnregistrerShiftAsync(
        IRepository<Planning> repo, List<Planning> existing, Dictionary<string, string> row, int noShift, CancellationToken ct)
    {
        ExcelCells.TryTime(ExcelCells.Get(row, "ENTREE", "HA", "Heure arrivée", "Arrivee"), out var ha);
        ExcelCells.TryTime(ExcelCells.Get(row, "PAUSE", "Pause"), out var pause);
        ExcelCells.TryTime(ExcelCells.Get(row, "SORTIE", "HD", "Heure départ", "Depart"), out var hd);
        var intitule = ExcelCells.Get(row, "INTITULE", "Intitulé", "Libelle");
        var entity = existing.FirstOrDefault(s => s.NoShift == noShift);
        if (entity == null)
        {
            entity = new Planning { NoShift = noShift, Ha = ha == default ? null : ha, Pause = pause == default ? null : pause, Hd = hd == default ? null : hd, Intitule = intitule };
            await repo.AddAsync(entity, ct);
            existing.Add(entity);
        }
        else
        {
            entity.Ha = ha == default ? entity.Ha : ha;
            entity.Pause = pause == default ? entity.Pause : pause;
            entity.Hd = hd == default ? entity.Hd : hd;
            if (!string.IsNullOrWhiteSpace(intitule))
                entity.Intitule = intitule;
            repo.Update(entity);
        }
    }
}
