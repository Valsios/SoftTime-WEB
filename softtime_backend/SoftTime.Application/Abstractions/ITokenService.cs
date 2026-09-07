using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface ITokenService
{
    string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights);
}
