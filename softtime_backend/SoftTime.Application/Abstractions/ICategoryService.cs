using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface ICategoryService
{
    Task<PagedResult<CategoryDto>> ListCategoriesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<Dictionary<int, string>> LoadCategoryLabelsAsync(CancellationToken ct);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default);

    Task<CategoryDto> SaveCategoryAsync(CategoryDto dto, CancellationToken ct = default);

    Task DeleteCategoryAsync(int id, CancellationToken ct = default);
}
