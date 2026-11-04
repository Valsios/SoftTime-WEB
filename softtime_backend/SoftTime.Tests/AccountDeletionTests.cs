using AutoMapper;
using Microsoft.AspNetCore.Identity;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Supprimer un compte efface ses droits personnels et ses accès aux bases, dans la même sauvegarde.</summary>
public class AccountDeletionTests
{
    private readonly SnapshotInMemoryUnitOfWork _store = new();
    private readonly RecordingUnitOfWork _uow;
    private readonly AuthService _auth;

    public AccountDeletionTests()
    {
        _uow = new RecordingUnitOfWork(_store);
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthService(_uow, new Token(), mapper, new Mail(), new AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<T_RESPONSABLE>());

        _store.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        _store.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 2, NOMROLE = "Utilisateur" });
        _store.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 1, IDROLE = 1, LOGIN = "admin" });
        _store.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 2, IDROLE = 2, LOGIN = "cible" });
        _store.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 3, IDROLE = 2, LOGIN = "autre" });
        foreach (var id in new decimal[] { 2, 3 })
        {
            _store.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = id, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });
            _store.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = id, PERMISSION_CODE = "REPORTS:REPORTS", IS_GRANTED = false });
            _store.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = (int)id, IDRESPONSABLE = id, IDBDD = 7 });
        }
        _store.GetRepo<T_AUDIT_DROIT>().Items.Add(new T_AUDIT_DROIT { ID_CIBLE = 2, LOGIN_OPERATEUR = "admin" });
    }

    private int Rows(decimal id) =>
        _store.GetRepo<T_USER_PERMISSION>().Items.Count(p => p.IDRESPONSABLE == id)
        + _store.GetRepo<T_BDD_AUTORISE>().Items.Count(a => a.IDRESPONSABLE == id);

    [Fact]
    public async Task Deleting_an_account_removes_its_permissions_and_database_access_only()
    {
        await _auth.DeleteUserAsync(1, 2);

        Assert.DoesNotContain(_store.GetRepo<T_RESPONSABLE>().Items, u => u.IDRESPONSABLE == 2);
        Assert.Equal(0, Rows(2));
        Assert.Equal(3, Rows(3));                                   // lignes des autres comptes intactes
        Assert.Single(_store.GetRepo<T_AUDIT_DROIT>().Items);        // journal d'audit conservé
    }

    [Fact]
    public async Task Everything_is_staged_before_one_single_save()
    {
        await _auth.DeleteUserAsync(1, 2);

        Assert.Equal(1, _uow.SaveCount);
        // Au moment de l'unique sauvegarde, le compte et toutes ses lignes étaient déjà retirés : une seule transaction.
        Assert.Equal((false, 0), _uow.StateAtSave);
    }

    [Fact]
    public async Task A_failed_save_is_the_only_save_attempted_by_the_service()
    {
        _uow.FailOnSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.DeleteUserAsync(1, 2));
        // Une seule sauvegarde (échouée) : avec une base réelle, la transaction annule toutes les suppressions ensemble.
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Self_deletion_is_refused_and_no_row_is_removed()
    {
        var before = _store.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.DeleteUserAsync(1, 1));
        Assert.Equal(before, _store.Snapshot());
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Last_administrator_deletion_is_refused_and_no_row_is_removed()
    {
        _store.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 1, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });
        var before = _store.Snapshot();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _auth.DeleteUserAsync(1));   // surcharge sans appelant
        Assert.Equal("Impossible de supprimer le dernier administrateur.", ex.Message);
        Assert.Equal(before, _store.Snapshot());
    }

    [Fact]
    public async Task Another_administrator_can_be_deleted_with_its_rows()
    {
        _store.GetRepo<T_RESPONSABLE>().Items.Add(new T_RESPONSABLE { IDRESPONSABLE = 9, IDROLE = 1, LOGIN = "admin2" });
        _store.GetRepo<T_USER_PERMISSION>().Items.Add(new T_USER_PERMISSION { IDRESPONSABLE = 9, PERMISSION_CODE = "PROC:SHIFTS", IS_GRANTED = true });
        await _auth.DeleteUserAsync(1, 9);
        Assert.Equal(0, Rows(9));
    }

    [Fact]
    public async Task Unknown_account_deletes_nothing()
    {
        var before = _store.Snapshot();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _auth.DeleteUserAsync(1, 999));
        Assert.Equal(before, _store.Snapshot());
    }

    /// <summary>Délègue au magasin en mémoire et mémorise l'état au moment de la sauvegarde.</summary>
    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        private readonly SnapshotInMemoryUnitOfWork _inner;
        public RecordingUnitOfWork(SnapshotInMemoryUnitOfWork inner) => _inner = inner;
        public int SaveCount { get; private set; }
        public bool FailOnSave { get; set; }
        public (bool UserStillThere, int RowsLeft)? StateAtSave { get; private set; }

        public IRepository<T> Repository<T>() where T : class => _inner.Repository<T>();
        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            StateAtSave = (_inner.GetRepo<T_RESPONSABLE>().Items.Any(u => u.IDRESPONSABLE == 2),
                _inner.GetRepo<T_USER_PERMISSION>().Items.Count(p => p.IDRESPONSABLE == 2)
                + _inner.GetRepo<T_BDD_AUTORISE>().Items.Count(a => a.IDRESPONSABLE == 2));
            if (FailOnSave) throw new InvalidOperationException("Échec simulé de la sauvegarde.");
            return Task.FromResult(1);
        }
    }

    private sealed class Token : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
