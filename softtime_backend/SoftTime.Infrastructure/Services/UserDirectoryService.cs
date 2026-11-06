using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class UserDirectoryService : IUserDirectoryService
{
    private readonly IUnitOfWork _uow;
    private readonly IParametersAccessGuard _parametersAccessGuard;

    public UserDirectoryService(
        IUnitOfWork uow,
        IParametersAccessGuard parametersAccessGuard)
    {
        _uow = uow;
        _parametersAccessGuard = parametersAccessGuard;
    }

    public async Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.Users, cancellationToken);
        var u = await _uow.Repository<Responsable>().GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException();
        return new UserDto(u.IdResponsable, u.IdRole, u.NomResponsable ?? string.Empty, u.Login ?? string.Empty, u.Matricule, null) { Email = u.Email };
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.UserList, cancellationToken);
        var list = await _uow.Repository<Responsable>().ListReadOnlyAsync(_ => true, cancellationToken);
        return list
            .OrderBy(u => u.NomResponsable)
            .ThenBy(u => u.Login)
            .Select(u => new UserDto(u.IdResponsable, u.IdRole, u.NomResponsable ?? string.Empty, u.Login ?? string.Empty, u.Matricule, null) { Email = u.Email })
            .ToList();
    }

    public async Task<PagedResult<UserDto>> GetUsersPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.Users, cancellationToken);
        decimal[] roleIds = Array.Empty<decimal>();
        if (SearchText.Terms(search).Count > 0)
        {
            roleIds = (await _uow.Repository<Role>().ListReadOnlyAsync(_ => true, cancellationToken))
                .Where(role => SearchText.Matches(search, role.NomRole))
                .Select(role => role.IdRole)
                .ToArray();
        }

        var searchPredicate = SearchExpression.For<Responsable>(
            search,
            u => u.NomResponsable,
            u => u.Login,
            u => u.Email,
            u => u.Matricule);
        System.Linq.Expressions.Expression<Func<Responsable, bool>> predicate =
            searchPredicate ?? (_ => true);
        if (roleIds.Length > 0)
        {
            System.Linq.Expressions.Expression<Func<Responsable, bool>> rolePredicate =
                user => roleIds.Contains(user.IdRole);
            predicate = searchPredicate is null
                ? rolePredicate
                : SearchExpression.OrElse(searchPredicate, rolePredicate);
        }

        return await PagedLists.PageAsync(
            _uow.Repository<Responsable>(),
            predicate,
            q => q.OrderBy(u => u.NomResponsable).ThenBy(u => u.Login).ThenBy(u => u.IdResponsable),
            page,
            pageSize,
            u => new UserDto(u.IdResponsable, u.IdRole, u.NomResponsable ?? string.Empty, u.Login ?? string.Empty, u.Matricule, null) { Email = u.Email },
            cancellationToken);
    }
}
