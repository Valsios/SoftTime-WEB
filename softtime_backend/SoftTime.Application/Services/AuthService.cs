using AutoMapper;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public class AuthService
{
    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IMapper _mapper;

    public AuthService(IUnitOfWork uow, ITokenService tokens, IMapper mapper)
    {
        _uow = uow;
        _tokens = tokens;
        _mapper = mapper;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var login = (request.Login ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        var users = await _uow.Repository<T_RESPONSABLE>().ListAsync(
            u => u.LOGIN == login && u.PASSWORD == password, cancellationToken);
        if (users.Count == 0)
        {
            users = (await _uow.Repository<T_RESPONSABLE>().ListAsync(_ => true, cancellationToken))
                .Where(u => string.Equals((u.LOGIN ?? string.Empty).Trim(), login, StringComparison.OrdinalIgnoreCase)
                            && (u.PASSWORD ?? string.Empty) == password)
                .ToList();
        }
        var user = users.FirstOrDefault() ?? throw new UnauthorizedAccessException("Identifiants invalides.");
        var privileges = await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == user.IDROLE, cancellationToken);
        var rights = privileges.Select(p => (int)p.IDDROIT).Distinct().ToList();
        var token = _tokens.CreateToken(user.IDRESPONSABLE, user.LOGIN ?? login, user.IDROLE, user.MATRICULE, rights);
        var access = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == user.IDRESPONSABLE, cancellationToken);
        var sageIds = access.Select(a => a.IDBDD).ToHashSet();
        var sages = await _uow.Repository<T_BDD_SAGE>().ListAsync(s => sageIds.Contains(s.ID), cancellationToken);
        return new LoginResponse(token, user.IDRESPONSABLE, user.LOGIN ?? login, user.NOMRESPONSABLE ?? login, user.IDROLE, user.MATRICULE, rights, sages.Select(_mapper.Map<SageDbDto>).ToList());
    }

    public async Task<LoginResponse> GetMeAsync(decimal userId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");
        var privileges = await _uow.Repository<T_DROIT_ROLE>().ListAsync(p => p.IDROLE == user.IDROLE, cancellationToken);
        var rights = privileges.Select(p => (int)p.IDDROIT).Distinct().ToList();
        var access = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == user.IDRESPONSABLE, cancellationToken);
        var sageIds = access.Select(a => a.IDBDD).ToHashSet();
        var sages = await _uow.Repository<T_BDD_SAGE>().ListAsync(s => sageIds.Contains(s.ID), cancellationToken);
        return new LoginResponse(string.Empty, user.IDRESPONSABLE, user.LOGIN ?? string.Empty, user.NOMRESPONSABLE ?? string.Empty, user.IDROLE, user.MATRICULE, rights, sages.Select(_mapper.Map<SageDbDto>).ToList());
    }

    public async Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var u = await _uow.Repository<T_RESPONSABLE>().GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        return new UserDto(u.IDRESPONSABLE, u.IDROLE, u.NOMRESPONSABLE ?? string.Empty, u.LOGIN ?? string.Empty, u.MATRICULE, null);
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var list = await _uow.Repository<T_RESPONSABLE>().ListAsync(_ => true, cancellationToken);
        return list
            .OrderBy(u => u.NOMRESPONSABLE)
            .ThenBy(u => u.LOGIN)
            .Select(u => new UserDto(u.IDRESPONSABLE, u.IDROLE, u.NOMRESPONSABLE ?? string.Empty, u.LOGIN ?? string.Empty, u.MATRICULE, null))
            .ToList();
    }

    public async Task<UserDto> SaveUserAsync(UserDto dto, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_RESPONSABLE>();
        T_RESPONSABLE entity;
        if (dto.Id == 0)
        {
            entity = new T_RESPONSABLE
            {
                IDROLE = dto.RoleId,
                NOMRESPONSABLE = dto.Nom,
                LOGIN = dto.Login,
                PASSWORD = dto.Password ?? string.Empty,
                MATRICULE = dto.Matricule
            };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new KeyNotFoundException();
            entity.IDROLE = dto.RoleId;
            entity.NOMRESPONSABLE = dto.Nom;
            entity.LOGIN = dto.Login;
            entity.MATRICULE = dto.Matricule;
            if (!string.IsNullOrWhiteSpace(dto.Password))
                entity.PASSWORD = dto.Password;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        return new UserDto(entity.IDRESPONSABLE, entity.IDROLE, entity.NOMRESPONSABLE, entity.LOGIN, entity.MATRICULE, null);
    }

    public async Task DeleteUserAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_RESPONSABLE>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
        => (await _uow.Repository<T_ROLE>().ListAsync(_ => true, cancellationToken))
            .Select(r => new RoleDto(r.IDROLE, r.NOMROLE)).ToList();

    public async Task<RoleDto> SaveRoleAsync(RoleDto dto, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_ROLE>();
        T_ROLE entity;
        if (dto.Id == 0)
        {
            entity = new T_ROLE { NOMROLE = dto.Nom };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, cancellationToken) ?? throw new KeyNotFoundException();
            entity.NOMROLE = dto.Nom;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        return new RoleDto(entity.IDROLE, entity.NOMROLE);
    }

    public async Task DeleteRoleAsync(decimal id, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_ROLE>();
        var entity = await repo.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException();
        repo.Remove(entity);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default)
        => (await _uow.Repository<T_DROIT>().ListAsync(_ => true, cancellationToken))
            .Select(d => new DroitDto(d.IDDROIT, d.DROIT)).ToList();

    public async Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default)
    {
        var list = await _uow.Repository<T_DROIT_ROLE>().ListAsync(
            p => !roleId.HasValue || p.IDROLE == roleId.Value, cancellationToken);
        return list.Select(p => new PrivilegeDto(p.IDDROITROLE, p.IDROLE, p.IDDROIT)).ToList();
    }

    public async Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_DROIT_ROLE>();
        var existing = await repo.ListAsync(p => p.IDROLE == roleId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var d in droitIds.Distinct())
            await repo.AddAsync(new T_DROIT_ROLE { IDROLE = roleId, IDDROIT = d }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default)
    {
        var list = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(
            a => !userId.HasValue || a.IDRESPONSABLE == userId.Value, cancellationToken);
        return list.Select(a => new DbAccessDto(a.IDBDDAUTORISE, a.IDRESPONSABLE, a.IDBDD)).ToList();
    }

    public async Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<T_BDD_AUTORISE>();
        var existing = await repo.ListAsync(a => a.IDRESPONSABLE == userId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var id in sageDbIds.Distinct())
            await repo.AddAsync(new T_BDD_AUTORISE { IDRESPONSABLE = userId, IDBDD = id }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
