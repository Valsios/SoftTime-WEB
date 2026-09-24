using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IExternalSourceReader
{
    Task<DepartementServiceDto> GetDepartementServiceAsync(
        string connectionString,
        SourceConfigDto config,
        string matricule,
        CancellationToken cancellationToken = default);
}