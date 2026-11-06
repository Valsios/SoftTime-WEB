using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

public interface ICorrespondenceModeService
{
    Task<CorrespondenceModeDto> GetCorrespondenceAsync(CancellationToken ct = default);

    Task<CorrespondenceModeDto> SetCorrespondenceAsync(bool active, CancellationToken ct = default);

    Task<SyncResultDto> SyncCardPaieCoreAsync(BddSage sageRow, BddPointeuse pteRow, CancellationToken ct);
}
