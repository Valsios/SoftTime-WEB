using System.Linq.Expressions;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class AdminRulesAndSafeguardsTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly FakeTokenService _tokens;
    private readonly IMapper _mapper;
    private readonly FakeEmailService _email;
    private readonly PasswordHasher<T_RESPONSABLE> _hasher;
    private readonly AuthService _auth;

    public AdminRulesAndSafeguardsTests()
    {
        _uow = new InMemoryUnitOfWork();
        _tokens = new FakeTokenService();

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();

        _email = new FakeEmailService();
        _hasher = new PasswordHasher<T_RESPONSABLE>();
        _auth = new AuthService(_uow, _tokens, _mapper, _email, _hasher);

        // Configuration initiale des rôles de référence
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Utilisateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 3, NOMROLE = "ROLE_ADMIN_CUSTOM" });
    }

    #region Tests AdminRules (G5, C3)

    [Fact]
    public void AdminRules_IsAdmin_IdentifiesAdminRoleId()
    {
        Assert.True(AdminRules.IsAdmin(1, null));
        Assert.True(AdminRules.IsAdmin(1, "AutreNom"));
    }

    [Fact]
    public void AdminRules_IsAdmin_IdentifiesRoleNameContainingAdmin_CaseInsensitive()
    {
        Assert.True(AdminRules.IsAdmin(2, "Administrateur"));
        Assert.True(AdminRules.IsAdmin(3, "ROLE_ADMIN_CUSTOM"));
        Assert.True(AdminRules.IsAdmin(4, "super-admin"));
        Assert.True(AdminRules.IsAdmin(5, "Administrateur RH"));
    }

    [Fact]
    public void AdminRules_IsAdmin_RejectsNonAdminRole()
    {
        Assert.False(AdminRules.IsAdmin(2, "Utilisateur"));
        Assert.False(AdminRules.IsAdmin(2, "Opérateur"));
        Assert.False(AdminRules.IsAdmin(2, null));
        Assert.False(AdminRules.IsAdmin(2, ""));
    }

    [Fact]
    public void AdminRules_IsAdmin_WithNullRoleEntity_ReturnsFalse()
    {
        T_ROLE? nullRole = null;
        Assert.False(AdminRules.IsAdmin(nullRole));
    }

    [Fact]
    public void AdminRules_IsSuperAdmin_ReturnsTrueOnlyForAdminRoleId()
    {
        Assert.True(AdminRules.IsSuperAdmin(1));
        Assert.False(AdminRules.IsSuperAdmin(2));
        Assert.False(AdminRules.IsSuperAdmin(3)); // Rôle dont le nom contient "ADMIN" mais IDROLE != 1
    }

    #endregion

    #region Tests C2 — Appelant Inconnu

    [Fact]
    public async Task SaveUserAsync_UnknownCaller_ThrowsInvalidOperationException()
    {
        var dto = new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(currentUserId: 999, dto));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task SaveUserAsync_CallerWithMissingRole_ThrowsInvalidOperationException()
    {
        var caller = new T_RESPONSABLE { IDRESPONSABLE = 10, IDROLE = 999, LOGIN = "callerOrphan", NOMRESPONSABLE = "Orphan" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(caller);

        var dto = new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(currentUserId: 10, dto));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_UnknownCaller_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.DeleteUserAsync(currentUserId: 999, id: 2));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    #endregion

    #region Tests G1 — Auto-modification de rôle

    [Fact]
    public async Task SaveUserAsync_SelfRoleModification_ThrowsInvalidOperationException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        var admin2 = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 1, LOGIN = "admin2", NOMRESPONSABLE = "Admin 2" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin2);

        // L'admin 1 tente de changer son propre rôle vers le rôle 2
        var updateDto = new UserDto(1, 2, "Admin Modifié", "admin", null, null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(currentUserId: 1, updateDto));
        Assert.Equal("Vous ne pouvez pas modifier votre propre rôle.", ex.Message);
    }

    [Fact]
    public async Task SaveUserAsync_SelfUpdateWithoutRoleChange_Succeeds()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin Initial", EMAIL = "old@softtime.local" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);

        var updateDto = new UserDto(1, 1, "Admin Nouveau Nom", "admin", "MAT-01", null) { Email = "new@softtime.local" };
        var result = await _auth.SaveUserAsync(currentUserId: 1, updateDto);

        Assert.Equal("Admin Nouveau Nom", result.Nom);
        Assert.Equal("new@softtime.local", result.Email);
        Assert.Equal(1, result.RoleId);
    }

    #endregion

    #region Tests G2 — Attribution rôle admin et modification compte admin

    [Fact]
    public async Task SaveUserAsync_NonAdmin_AssigningAdminRole_ThrowsInvalidOperationException()
    {
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "User" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        // Un utilisateur non-admin tente de créer un admin
        var createDto = new UserDto(0, 1, "Nouvel Admin", "newadmin", null, "ValidP@ss123!");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(currentUserId: 2, createDto));
        Assert.Equal("Seul un administrateur peut attribuer le rôle Administrateur.", ex.Message);
    }

    [Fact]
    public async Task SaveUserAsync_NonAdmin_ModifyingAdminAccount_ThrowsInvalidOperationException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "User" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        // L'utilisateur tente de modifier le mot de passe de l'admin
        var updateDto = new UserDto(1, 1, "Admin Hacké", "admin", null, "NewPass123!@");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(currentUserId: 2, updateDto));
        Assert.Equal("Seul un administrateur peut modifier un compte administrateur.", ex.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_NonAdmin_DeletingAdminAccount_ThrowsInvalidOperationException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "User" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.DeleteUserAsync(currentUserId: 2, id: 1));
        Assert.Equal("Seul un administrateur peut supprimer un compte administrateur.", ex.Message);
    }

    [Fact]
    public async Task SaveUserAsync_Admin_AssigningAdminRole_Succeeds()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);

        var createDto = new UserDto(0, 1, "Admin 2", "admin2", null, "ValidP@ss123!");
        var result = await _auth.SaveUserAsync(currentUserId: 1, createDto);

        Assert.Equal(1, result.RoleId);
        Assert.Equal("admin2", result.Login);
    }

    #endregion

    #region Tests G3 — Dernier Administrateur

    [Fact]
    public async Task SaveUserAsync_DemotingOnlyAdmin_ThrowsInvalidOperationException()
    {
        var admin1 = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin1", NOMRESPONSABLE = "Admin 1" };
        var admin2 = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 1, LOGIN = "admin2", NOMRESPONSABLE = "Admin 2" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin1);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin2);

        // Admin 2 rétrograde Admin 1 vers le rôle 2 -> Permis car Admin 2 reste administrateur
        var updateDto = new UserDto(1, 2, "Admin 1", "admin1", null, null);
        var res = await _auth.SaveUserAsync(currentUserId: 2, updateDto);
        Assert.Equal(2, res.RoleId);

        // Maintenant Admin 1 est rôle 2. Si un admin tente de rétrograder le seul admin restant (Admin 2):
        // Note: Admin 2 ne peut pas rétrograder son propre rôle (G1), mais créons un test où l'appelant tente de rétrograder le dernier admin restant
        // Simulons un troisième compte avec un rôle de délégation ou appel direct
        var updateLastAdmin = new UserDto(2, 2, "Admin 2", "admin2", null, null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(updateLastAdmin));
        Assert.Equal("Impossible de rétrograder le dernier administrateur.", ex.Message);
    }

    [Fact]
    public async Task SaveUserAsync_G3_IgnoresCustomRoleContainingAdminWhenCountingSuperAdmins()
    {
        // Un seul vrai admin (IDROLE = 1) et un compte avec un rôle custom dont le nom contient "ADMIN" (IDROLE = 3)
        var superAdmin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "superadmin", NOMRESPONSABLE = "Super Admin" };
        var customAdmin = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 3, LOGIN = "customadmin", NOMRESPONSABLE = "Custom Admin" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(superAdmin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(customAdmin);

        // Tenter de rétrograder le seul superAdmin (IDROLE = 1) vers rôle 2
        var updateDto = new UserDto(1, 2, "Super Admin", "superadmin", null, null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.SaveUserAsync(currentUserId: 2, updateDto));
        Assert.Equal("Impossible de rétrograder le dernier administrateur.", ex.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_DeletingOnlyAdmin_ThrowsInvalidOperationException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);

        // Surcharge de test sans appelant
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.DeleteUserAsync(1));
        Assert.Equal("Impossible de supprimer le dernier administrateur.", ex.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_DeletingAdminWhenAnotherExists_Succeeds()
    {
        var admin1 = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin1", NOMRESPONSABLE = "Admin 1" };
        var admin2 = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 1, LOGIN = "admin2", NOMRESPONSABLE = "Admin 2" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin1);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin2);

        // Admin 1 supprime Admin 2
        await _auth.DeleteUserAsync(currentUserId: 1, id: 2);

        Assert.Null(await _uow.GetRepo<T_RESPONSABLE>().GetByIdAsync(2));
        Assert.NotNull(await _uow.GetRepo<T_RESPONSABLE>().GetByIdAsync(1));
    }

    #endregion

    #region Tests G4 — Auto-suppression

    [Fact]
    public async Task DeleteUserAsync_SelfDelete_ThrowsInvalidOperationException()
    {
        var admin1 = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin1", NOMRESPONSABLE = "Admin 1" };
        var admin2 = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 1, LOGIN = "admin2", NOMRESPONSABLE = "Admin 2" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin1);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.DeleteUserAsync(currentUserId: 1, id: 1));
        Assert.Equal("Vous ne pouvez pas supprimer votre propre compte.", ex.Message);
    }

    #endregion

    #region In-Memory Test Doubles

    private sealed class InMemoryRepository<T> : IRepository<T> where T : class
    {
        public List<T> Items { get; } = new();

        public IQueryable<T> Query() => Items.AsQueryable();

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            var decId = Convert.ToDecimal(id);
            var match = Items.FirstOrDefault(i =>
            {
                if (i is T_RESPONSABLE r && r.IDRESPONSABLE == decId) return true;
                if (i is T_ROLE ro && ro.IDROLE == decId) return true;
                return false;
            });
            return Task.FromResult(match);
        }

        public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            return Task.FromResult(Items.Where(func).ToList());
        }

        public Task<List<T>> ListLatestAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<T, DateTime?>> orderByDescending,
            int take,
            CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            var orderFunc = orderByDescending.Compile();
            return Task.FromResult(Items.Where(func).OrderByDescending(orderFunc).Take(take).ToList());
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is T_RESPONSABLE r && r.IDRESPONSABLE == 0)
            {
                r.IDRESPONSABLE = Items.Count == 0 ? 1 : Items.OfType<T_RESPONSABLE>().Max(x => x.IDRESPONSABLE) + 1;
            }
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            foreach (var e in entities)
                AddAsync(e, cancellationToken);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Remove(T entity) => Items.Remove(entity);
        public void RemoveRange(IEnumerable<T> entities)
        {
            foreach (var e in entities.ToList())
                Items.Remove(e);
        }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repos = new();

        public InMemoryRepository<T> GetRepo<T>() where T : class
        {
            if (!_repos.TryGetValue(typeof(T), out var r))
            {
                r = new InMemoryRepository<T>();
                _repos[typeof(T)] = r;
            }
            return (InMemoryRepository<T>)r;
        }

        public IRepository<T> Repository<T>() where T : class => GetRepo<T>();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights)
            => $"FAKE_JWT_TOKEN_{userId}_{login}";
    }

    private sealed class FakeEmailService : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    #endregion
}
