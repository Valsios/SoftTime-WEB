using System.Linq.Expressions;

using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
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
        _auth = new AuthService(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

        // Configuration initiale des rôles de référence
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Utilisateur" });
        _uow.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 3, NOMROLE = "ROLE_ADMIN_CUSTOM" });
    }

    /// <summary>Service dont l'appelant HTTP (ICurrentUser) est le compte indiqué ; _auth, lui, est toujours un administrateur.</summary>
    private AuthService CallerAuth(decimal userId, decimal roleId)
        => new(_uow, _tokens, _mapper, _email, new SoftTime.Tests.Http.TestCurrentUser(userId, roleId), SoftTime.Tests.Http.TestRights.Service(_uow), _hasher);

    #region Tests AdminRules (G5, C3)

    [Fact]
    public void AdminRules_IsAdmin_IdentifiesAdminRoleId()
    {
        Assert.True(AdminRules.IsAdmin(1, null));
        Assert.True(AdminRules.IsAdmin(1, "AutreNom"));
    }

    [Fact]
    public void AdminRules_IsAdmin_DoesNotRecognizeRoleNameContainingAdmin()
    {
        // Règle 4b-2 : seul le rôle 1 est administrateur, le nom du rôle ne confère rien (avant : ces quatre cas étaient vrais).
        Assert.False(AdminRules.IsAdmin(2, "Administrateur"));
        Assert.False(AdminRules.IsAdmin(3, "ROLE_ADMIN_CUSTOM"));
        Assert.False(AdminRules.IsAdmin(4, "super-admin"));
        Assert.False(AdminRules.IsAdmin(5, "Administrateur RH"));
        Assert.False(AdminRules.IsAdmin(new T_ROLE { IDROLE = 3, NOMROLE = "ROLE_ADMIN_CUSTOM" }));
        Assert.True(AdminRules.IsAdmin(new T_ROLE { IDROLE = 1, NOMROLE = "Autre nom" }));
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
    public async Task SaveUserAsync_UnknownCaller_ThrowsDomainException()
    {
        var dto = new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 999, dto));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task SaveUserAsync_CallerWithMissingRole_ThrowsDomainException()
    {
        var caller = new T_RESPONSABLE { IDRESPONSABLE = 10, IDROLE = 999, LOGIN = "callerOrphan", NOMRESPONSABLE = "Orphan" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(caller);

        var dto = new UserDto(0, 2, "Nouveau", "nouveau", null, "ValidP@ss123!");
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 10, dto));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_UnknownCaller_ThrowsDomainException()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.DeleteUserAsync(currentUserId: 999, id: 2));
        Assert.Equal("Utilisateur appelant introuvable.", ex.Message);
    }

    #endregion

    #region Tests G1 — Auto-modification de rôle

    [Fact]
    public async Task SaveUserAsync_SelfRoleModification_ThrowsDomainException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        var admin2 = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 1, LOGIN = "admin2", NOMRESPONSABLE = "Admin 2" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin2);

        // L'admin 1 tente de changer son propre rôle vers le rôle 2
        var updateDto = new UserDto(1, 2, "Admin Modifié", "admin", null, null);
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 1, updateDto));
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
    public async Task SaveUserAsync_NonAdmin_AssigningAdminRole_ThrowsDomainException()
    {
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "User" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        // Un utilisateur non-admin tente de créer un super-admin (rôle 1)
        var createDto = new UserDto(0, 1, "Nouvel Admin", "newadmin", null, "ValidP@ss123!");
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 2, createDto));
        Assert.Equal("Seul un super-administrateur peut attribuer le rôle Administrateur.", ex.Message);
    }

    [Fact]
    public async Task SaveUserAsync_NonAdmin_ModifyingAdminAccount_ThrowsDomainException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "User" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        // L'utilisateur tente de modifier le mot de passe du super-admin (rôle 1)
        var updateDto = new UserDto(1, 1, "Admin Hacké", "admin", null, "NewPass123!@");
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 2, updateDto));
        Assert.Equal("Seul un super-administrateur peut modifier un compte administrateur.", ex.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_NonAdmin_DeletingAdminAccount_ThrowsDomainException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "User" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.DeleteUserAsync(currentUserId: 2, id: 1));
        Assert.Equal("Seul un super-administrateur peut supprimer un compte administrateur.", ex.Message);
    }

    [Fact]
    public async Task NonAdmin_CustomAdminRoleOperations_AreRefusedByTheAdministratorGuard()
    {
        // Règle 4b-2 : le rôle 3 « ROLE_ADMIN_CUSTOM » n'est plus un rôle administrateur. Toute opération sur des comptes
        // par un appelant du rôle 2 est refusée dès l'entrée (avant : InvalidOperationException « Seul un administrateur… »).
        var user = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user", NOMRESPONSABLE = "User" };
        var customAdmin = new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "customadmin", NOMRESPONSABLE = "Custom Admin" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(customAdmin);
        var asUser = CallerAuth(2, 2);

        var createDto = new UserDto(0, 3, "New Custom Admin", "newcustom", null, "ValidP@ss123!");
        var exCreate = await Assert.ThrowsAsync<ForbiddenException>(() => asUser.SaveUserAsync(currentUserId: 2, createDto));
        Assert.Equal(AdminAccess.ReservedMessage, exCreate.Message);

        var updateDto = new UserDto(3, 3, "Custom Admin Modifié", "customadmin", null, null);
        var exMod = await Assert.ThrowsAsync<ForbiddenException>(() => asUser.SaveUserAsync(currentUserId: 2, updateDto));
        Assert.Equal(AdminAccess.ReservedMessage, exMod.Message);

        var exDel = await Assert.ThrowsAsync<ForbiddenException>(() => asUser.DeleteUserAsync(currentUserId: 2, id: 3));
        Assert.Equal(AdminAccess.ReservedMessage, exDel.Message);
        Assert.Equal("Custom Admin", _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 3).NOMRESPONSABLE);
    }

    [Fact]
    public async Task SaveUserAsync_CustomAdmin_CannotAssignRole1_CreationAndModification()
    {
        var customAdmin = new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "customadmin", NOMRESPONSABLE = "Custom Admin" };
        var ordinaryUser = new T_RESPONSABLE { IDRESPONSABLE = 4, IDROLE = 2, LOGIN = "user4", NOMRESPONSABLE = "User 4" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(customAdmin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(ordinaryUser);

        // Création avec rôle 1 par un admin personnalisé
        var createDto = new UserDto(0, 1, "New SuperAdmin", "newsuper", null, "ValidP@ss123!");
        var exCreate = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 3, createDto));
        Assert.Equal("Seul un super-administrateur peut attribuer le rôle Administrateur.", exCreate.Message);

        // Modification avec rôle 1 par un admin personnalisé
        var updateDto = new UserDto(4, 1, "User 4 Promu", "user4", null, null);
        var exUpdate = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 3, updateDto));
        Assert.Equal("Seul un super-administrateur peut attribuer le rôle Administrateur.", exUpdate.Message);
    }

    [Fact]
    public async Task CustomAdmin_CannotModifyOrDeleteRole1Account_EvenIfAnotherSuperAdminExists()
    {
        var admin1 = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin1", NOMRESPONSABLE = "Admin 1" };
        var admin2 = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 1, LOGIN = "admin2", NOMRESPONSABLE = "Admin 2" };
        var customAdmin = new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "customadmin", NOMRESPONSABLE = "Custom Admin" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin1);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin2);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(customAdmin);

        // Tentative de modification du compte rôle 1
        var updateDto = new UserDto(1, 1, "Admin 1 Renommé", "admin1", null, null);
        var exMod = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(currentUserId: 3, updateDto));
        Assert.Equal("Seul un super-administrateur peut modifier un compte administrateur.", exMod.Message);

        // Tentative de suppression du compte rôle 1
        var exDel = await Assert.ThrowsAsync<DomainException>(() => _auth.DeleteUserAsync(currentUserId: 3, id: 1));
        Assert.Equal("Seul un super-administrateur peut supprimer un compte administrateur.", exDel.Message);
    }

    [Fact]
    public async Task CustomAdmin_CannotCreateModifyOrDeleteOrdinaryAccount_AfterRule4b2()
    {
        // Règle 4b-2 (avant : un rôle dont le nom contient ADMIN pouvait créer, modifier et supprimer un compte ordinaire).
        var customAdmin = new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 3, LOGIN = "customadmin", NOMRESPONSABLE = "Custom Admin" };
        var ordinary = new T_RESPONSABLE { IDRESPONSABLE = 5, IDROLE = 2, LOGIN = "employe1", NOMRESPONSABLE = "Employé 1" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(customAdmin);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(ordinary);
        var asCustom = CallerAuth(3, 3);

        await Assert.ThrowsAsync<ForbiddenException>(() => asCustom.SaveUserAsync(currentUserId: 3, new UserDto(0, 2, "Employé 2", "employe2", null, "ValidP@ss123!")));
        await Assert.ThrowsAsync<ForbiddenException>(() => asCustom.SaveUserAsync(currentUserId: 3, new UserDto(5, 2, "Employé 1 Modifié", "employe1_mod", null, null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => asCustom.DeleteUserAsync(currentUserId: 3, id: 5));
        Assert.Equal("employe1", _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 5).LOGIN);
    }

    [Fact]
    public async Task OrdinaryUser_CannotCreateModifyOrDeleteAccounts_AfterStep4b1()
    {
        // Étape 4b-1 (avant : un compte ordinaire pouvait gérer des comptes ordinaires au niveau du service).
        var ordinaryCaller = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "user2", NOMRESPONSABLE = "User 2" };
        var other = new T_RESPONSABLE { IDRESPONSABLE = 6, IDROLE = 2, LOGIN = "employe3", NOMRESPONSABLE = "Employé 3" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(ordinaryCaller);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(other);
        var asOrdinary = CallerAuth(2, 2);

        await Assert.ThrowsAsync<ForbiddenException>(() => asOrdinary.SaveUserAsync(currentUserId: 2, new UserDto(0, 2, "Employé 2", "employe2", null, "ValidP@ss123!")));
        await Assert.ThrowsAsync<ForbiddenException>(() => asOrdinary.SaveUserAsync(currentUserId: 2, new UserDto(6, 2, "Employé 3 Modifié", "employe3_mod", null, null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => asOrdinary.DeleteUserAsync(currentUserId: 2, id: 6));
        Assert.Equal("employe3", _uow.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == 6).LOGIN);
    }

    [Fact]
    public async Task SuperAdmin_CanAssignRole1()
    {
        var superAdmin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(superAdmin);

        // Création d'un nouvel administrateur rôle 1
        var createDto = new UserDto(0, 1, "Admin 2", "admin2", null, "ValidP@ss123!");
        var created = await _auth.SaveUserAsync(currentUserId: 1, createDto);
        Assert.Equal(1, created.RoleId);
        Assert.Equal("admin2", created.Login);

        // Promotion d'un compte ordinaire vers le rôle 1
        var user = new T_RESPONSABLE { IDRESPONSABLE = 10, IDROLE = 2, LOGIN = "user10", NOMRESPONSABLE = "User 10" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(user);
        var updateDto = new UserDto(10, 1, "User 10 Promu", "user10", null, null);
        var promoted = await _auth.SaveUserAsync(currentUserId: 1, updateDto);
        Assert.Equal(1, promoted.RoleId);
    }

    #endregion

    #region Tests G3 — Dernier Administrateur

    [Fact]
    public async Task SaveUserAsync_DemotingOnlyAdmin_ThrowsDomainException()
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
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(updateLastAdmin));
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

        // Tenter de rétrograder le seul superAdmin (IDROLE = 1) vers rôle 2 via la surcharge sans appelant
        var updateDto = new UserDto(1, 2, "Super Admin", "superadmin", null, null);
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.SaveUserAsync(updateDto));
        Assert.Equal("Impossible de rétrograder le dernier administrateur.", ex.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_DeletingOnlyAdmin_ThrowsDomainException()
    {
        var admin = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin);

        // Surcharge de test sans appelant
        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.DeleteUserAsync(1));
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
    public async Task DeleteUserAsync_SelfDelete_ThrowsDomainException()
    {
        var admin1 = new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin1", NOMRESPONSABLE = "Admin 1" };
        var admin2 = new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 1, LOGIN = "admin2", NOMRESPONSABLE = "Admin 2" };
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin1);
        _uow.GetRepo<T_RESPONSABLE>().Items.Add(admin2);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _auth.DeleteUserAsync(currentUserId: 1, id: 1));
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
