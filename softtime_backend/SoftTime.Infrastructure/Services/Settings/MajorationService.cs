using AutoMapper;

using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Settings;

public sealed class MajorationService : IMajorationService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public MajorationService(
        IUnitOfWork uow,
        IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<PagedResult<MajorationDto>> ListMajorationsPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
    {
        var list = await _uow.Repository<Majoration>().ListReadOnlyAsync(_ => true, ct);
        var rows = list
            .OrderBy(m => m.Cotation)
            .ThenBy(m => m.Id)
            .Select(_mapper.Map<MajorationDto>)
            .Where(m => SearchText.Matches(search, m.Mojoration, m.Cotation))
            .ToList();
        return PagedLists.Page(rows, page, pageSize);
    }

    public async Task<IReadOnlyList<MajorationDto>> ListMajorationsAsync(CancellationToken ct = default)
    {
        // Desktop (RExtensions.getListMajoration) returns every row: the SAGE/pointeuse
        // filter is commented out, and existing data often has BDD_SAGE null.
        var list = await _uow.Repository<Majoration>().ListReadOnlyAsync(_ => true, ct);
        return list.OrderBy(m => m.Cotation).Select(_mapper.Map<MajorationDto>).ToList();
    }

    public async Task<MajorationDto> SaveMajorationAsync(MajorationDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Majoration>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
        entity.Cotation = dto.Cotation;
        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<MajorationDto>(entity);
    }
}
