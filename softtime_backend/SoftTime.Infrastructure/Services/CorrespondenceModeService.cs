using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services.CardSync;

namespace SoftTime.Infrastructure.Services;

public sealed class CorrespondenceModeService : ICorrespondenceModeService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public CorrespondenceModeService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
    }

    // Le mode correspondance est rattache a la paire
    // selectionnee par l'utilisateur
    public async Task<CorrespondenceModeDto> GetCorrespondenceAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var row = (await _uow.Repository<CorrespondanceActive>().ListReadOnlyAsync(a =>
            a.SageServeur == sageRow.Serveur && a.SageBdd == sageRow.NomBd &&
            a.PointeuseServeur == pteRow.Serveur && a.PointeuseBdd == pteRow.NomBd, ct)).FirstOrDefault();
        return new CorrespondenceModeDto(row?.Active == true);
    }

    public async Task<CorrespondenceModeDto> SetCorrespondenceAsync(bool active, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var repo = _uow.Repository<CorrespondanceActive>();
        var row = (await repo.ListAsync(a =>
            a.SageServeur == sageRow.Serveur && a.SageBdd == sageRow.NomBd &&
            a.PointeuseServeur == pteRow.Serveur && a.PointeuseBdd == pteRow.NomBd, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new CorrespondanceActive
            {
                Active = active,
                SageServeur = sageRow.Serveur,
                SageBdd = sageRow.NomBd,
                PointeuseServeur = pteRow.Serveur,
                PointeuseBdd = pteRow.NomBd,
                DateModif = DateTime.Now
            };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.Active = active;
            row.DateModif = DateTime.Now;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        if (!active)
        {
            // Desactivation du mode manuel : on relance immediatement l'auto-mapping
            var result = await SyncCardPaieCoreAsync(sageRow, pteRow, ct);
            row.DerniereSync = DateTime.Now;
            repo.Update(row);
            await _uow.SaveChangesAsync(ct);
        }
        return new CorrespondenceModeDto(active);
    }

    public async Task<SyncResultDto> SyncCardPaieCoreAsync(BddSage sageRow, BddPointeuse pteRow, CancellationToken ct)
    {
        var source = await CardPaieSyncSource.LoadAsync(_externalSource, sageRow, pteRow, ct);
        return await new CardPaieSynchronization(_uow, sageRow, pteRow, source).RunAsync(ct);
    }
}
