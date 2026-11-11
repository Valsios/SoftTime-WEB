using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

using static SoftTime.Application.Security.EffectiveRightsCalculator;

namespace SoftTime.Tests;

/// <summary>Étape 3B : calcul pur des droits effectifs (rôle + ajouts - retraits). Valeurs factices, aucune donnée réelle.</summary>
public class EffectiveRightsTests
{
    private const int Databases = 1, Parameters = 2, Processing = 3, Other = 4, Traceability = 5, Reports = 6;

    private static readonly DatabaseCatalog Dbs = new(
        new[] { new DatabaseRef(1, "BASE1"), new DatabaseRef(2, "BASE2") }, new[] { new DatabaseRef(1, "PTG") }, new[] { 1 });

    private static RoleDefinition Role(decimal id, string[]? codes = null)
        => new(id, true, (codes ?? Array.Empty<string>()).Select(c => new PermissionRow(c)).ToList());

    private static PermissionRow[] Add(params string[] codes) => codes.Select(c => new PermissionRow(c, true)).ToArray();
    private static PermissionRow[] Remove(params string[] codes) => codes.Select(c => new PermissionRow(c, false)).ToArray();
    private static PermissionRow[] None => Array.Empty<PermissionRow>();

    private static EffectiveRights Compute(RoleDefinition role, PermissionRow[]? user = null, DatabaseCatalog? dbs = null)
        => ComputeEffectiveRights(role, user ?? None, dbs ?? Dbs);

    private static readonly string[] SalesRole = { "shifts.read", "punches.manage", "reports.read", "sage.1.read" };

    #region Matrice de base

    [Fact]
    public void Compute_RoleOnly_GivesRoleRightsAndDeducedGates()
    {
        // Arrange
        var r = Compute(Role(5, SalesRole));

        // Act
        var result = r.Permissions.SetEquals(SalesRole);

        // Assert
        result.Should().BeTrue();
        r.Gates.SetEquals(new[] { Databases, Processing, Reports }).Should().BeTrue();
        r.IsCustomized.Should().BeFalse();
        r.Inconsistencies.Should().BeEmpty();
        r.SensitiveRights.Should().BeEmpty();
    }

    [Fact]
    public void Compute_Addition_GrantsRightAndItsGate()
    {
        // Arrange
        var sut = Compute(Role(5, SalesRole), Add("users.read"));

        // Act
        var result = sut.Has("users.read");

        // Assert
        result.Should().BeTrue();
        sut.HasGate(Parameters).Should().BeTrue();
        sut.IsCustomized.Should().BeTrue();
        sut.SensitiveRights.Should().Equal(new[] { "users.read" });
    }

    [Fact]
    public void Compute_Removal_RemovesRightAndEmptyGate()
    {
        // Arrange
        var sut = Compute(Role(5, SalesRole), Remove("shifts.read", "punches.manage"));

        // Act
        var result = sut.Has("shifts.read");

        // Assert
        result.Should().BeFalse();
        sut.HasGate(Processing).Should().BeFalse();
        sut.HasGate(Reports).Should().BeTrue();
        sut.IsCustomized.Should().BeTrue();
    }

    [Fact]
    public void Compute_AdditionAndRemoval_AppliesBoth()
    {
        // Arrange
        var sut = Compute(Role(5, SalesRole), Add("canteen.manage").Concat(Remove("reports.read")).ToArray());

        // Act & Assert
        sut.Has("canteen.manage").Should().BeTrue();
        sut.Has("reports.read").Should().BeFalse();
        sut.Gates.SetEquals(new[] { Databases, Processing, Other }).Should().BeTrue();
    }

    [Fact]
    public void Compute_Administrator_HasEverythingAndIsNotModifiable()
    {
        // Arrange
        var withRows = Compute(Role(1, new[] { "reports.read" }), Remove("users.read", "sage.1.read").Concat(Add("INCONNU:X")).ToArray());

        // Act
        var plain = Compute(Role(1));

        // Assert
        withRows.IsAdministrator.Should().BeTrue();
        withRows.Permissions.SetEquals(plain.Permissions).Should().BeTrue();
        withRows.Gates.SetEquals(new[] { 1, 2, 3, 4, 5, 6 }).Should().BeTrue();
        withRows.IsCustomized.Should().BeFalse();
        withRows.SensitiveRights.Should().Equal(SensitiveCodes);
        withRows.Inconsistencies.Should().BeEmpty();
        (withRows.Has("sage.all.update") && withRows.Has("pointeuse.create")).Should().BeTrue();
        // Seul le rôle 1 est administrateur : un rôle nommé comme tel n'a rien de spécial.
        Compute(Role(3, new[] { "reports.read" })).IsAdministrator.Should().BeFalse();
    }

    #endregion

    #region Rôle sans lignes (comme 10032) et marqueurs

    [Fact]
    public void RightsFromLegacyGates_ThenCompute_GivesGateRightsWithAllDatabasesReadOnly()
    {
        // Arrange : la porte Bases donnait la lecture de toutes les bases, y compris futures : joker « all ».
        var gates = new[] { Databases, Processing, Other, Traceability, Reports };
        var r = Compute(Role(10032, RightsFromLegacyGates(gates).ToArray()));
        var configuration = new[] { "sage.create", "sage.all.update", "sage.all.delete", "pointeuse.create", "pointeuse.all.update", "pointeuse.all.delete" };

        // Act
        var result = r.Permissions.SetEquals(PermissionCatalog.BuildDefaultPermissionsList(gates.ToHashSet()).Except(configuration));

        // Assert
        result.Should().BeTrue();
        (r.Has("sage.all.read") && r.Has("pointeuse.all.read")).Should().BeTrue();
        r.Gates.SetEquals(gates).Should().BeTrue();
        r.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Create).Should().BeFalse();
        r.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1").Should().BeFalse();
        r.CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeTrue();
        r.CanUseDatabase(DatabaseKind.Sage, "BASE2").Should().BeFalse();       // non cochée dans « Accès bases »
        r.CanUseDatabase(DatabaseKind.Pointeuse, "PTG").Should().BeTrue();
        r.CanUseDatabase(DatabaseKind.Sage, "INCONNUE").Should().BeFalse();
    }

    [Fact]
    public void Compute_RoleWithoutRows_HasNoRightNorGate()
    {
        // Arrange : les portes ne donnent plus de droits par elles-mêmes (droits détaillés seulement).
        var sut = Compute(Role(7, null), None, DatabaseCatalog.Empty);

        // Act
        var result = sut.HasGate(Databases);

        // Assert
        result.Should().BeFalse();
        sut.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void Compute_RoleMarker_GivesNoRightsAndNoDefaults()
    {
        // Act
        var r = Compute(Role(8, new[] { PermissionCatalog.EmptyRoleMarker }));

        // Assert
        r.Permissions.Should().BeEmpty();
        r.Gates.Should().BeEmpty();
    }

    [Fact]
    public void Compute_UserMarker_RemovesRoleRightsButKeepsAdditions()
    {
        // Arrange
        var role = Role(5, SalesRole);

        // Act
        var none = Compute(role, Add(PermissionCatalog.EmptyUserMarker));

        // Assert
        none.Permissions.Should().BeEmpty();
        none.Gates.Should().BeEmpty();
        none.IsCustomized.Should().BeTrue();
        var plusOne = Compute(role, Add(PermissionCatalog.EmptyUserMarker, "reports.read"));
        plusOne.Permissions.SetEquals(new[] { "reports.read" }).Should().BeTrue();
        plusOne.Gates.SetEquals(new[] { Reports }).Should().BeTrue();
    }

    #endregion

    #region Changement de rôle, portes, droits sensibles

    [Fact]
    public void Compute_RoleChange_KeepsDeviationsOnNewRole()
    {
        // Arrange
        var user = Add("users.read").Concat(Remove("shifts.read")).ToArray();
        var a = Compute(Role(5, new[] { "shifts.read", "reports.read" }), user);
        var b = Compute(Role(6, new[] { "shifts.read", "canteen.manage" }), user);

        // Act
        var result = a.Permissions.SetEquals(new[] { "reports.read", "users.read" });

        // Assert
        result.Should().BeTrue();
        b.Permissions.SetEquals(new[] { "canteen.manage", "users.read" }).Should().BeTrue();
    }

    [Fact]
    public void Compute_AnyRights_DeducesGatesFromEffectiveRightsOnly()
    {
        // Arrange
        var r = Compute(Role(9, new[] { "sage.1.read", "roles.read", "shifts.read", "canteen.manage", "audit.read", "reports.read" }));

        // Act
        var result = r.Gates.SetEquals(new[] { 1, 2, 3, 4, 5, 6 });

        // Assert
        result.Should().BeTrue();
        // Les portes de T_DROIT_ROLE ne comptent pas quand le rôle a des lignes détaillées.
        Compute(Role(9, new[] { "reports.read" })).Gates.SetEquals(new[] { Reports }).Should().BeTrue();
    }

    [Fact]
    public void Compute_SensitiveRights_ListsTheFourRightsChangingRights()
    {
        // Act
        var r = Compute(Role(9, new[] { "db-access.read", "users.read", "card-paie.read" }), Add("privileges.read", "roles.read"));

        // Assert
        r.SensitiveRights.Should().Equal(new[] { "users.read", "roles.read", "privileges.read", "db-access.read" });
    }

    #endregion

    #region Incohérence « porte de travail sans accès aux bases »

    [Theory]
    [InlineData(Processing, "shifts.read")]
    [InlineData(Other, "canteen.manage")]
    [InlineData(Reports, "reports.read")]
    public void Compute_WorkGateWithoutDatabaseAccess_IsReportedNotBlocked(int gate, string code)
    {
        // Act
        // Aucune base cochée dans « Accès bases ».
        var none = Compute(Role(5, new[] { code, "sage.1.read" }), None, new DatabaseCatalog(Dbs.Sage, Dbs.Pointeuse, Array.Empty<int>()));

        // Assert
        none.Inconsistencies.Should().ContainSingle().Which.Gate.Should().Be(gate);
        none.Has(code).Should().BeTrue();
        // Base cochée mais sans permission de lecture.
        var noRead = Compute(Role(5, new[] { code, "sage.2.read" }));
        noRead.Inconsistencies.Should().ContainSingle();
        // Base cochée et lecture accordée : cohérent.
        Compute(Role(5, new[] { code, "sage.1.read" })).Inconsistencies.Should().BeEmpty();
    }

    [Fact]
    public void Compute_NonWorkGatesOrAdministrator_AreNeverInconsistent()
    {
        var noAccess = new DatabaseCatalog(Dbs.Sage, Dbs.Pointeuse, Array.Empty<int>());
        Compute(Role(5, new[] { "users.read", "audit.read" }), None, noAccess).Inconsistencies.Should().BeEmpty();
        Compute(Role(1), None, noAccess).Inconsistencies.Should().BeEmpty();
    }

    [Fact]
    public void Compute_SeveralWorkGates_GiveOneInconsistencyEach()
    {
        // Arrange
        var r = Compute(Role(5, new[] { "shifts.read", "reports.read" }), None, DatabaseCatalog.Empty);

        // Act
        var result = r.Inconsistencies.Select(i => i.Gate).OrderBy(g => g);

        // Assert
        result.Should().Equal(new[] { Processing, Reports });
    }

    #endregion

    #region Utiliser / configurer une base (R4)

    [Fact]
    public void CanUseDatabase_Sage_NeedsGate1CheckedDatabaseAndRead()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "sage.1.read" }));

        // Act & Assert
        sut.CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Sage, "base1").Should().BeTrue();
        Compute(Role(5, new[] { "sage.2.read" })).CanUseDatabase(DatabaseKind.Sage, "BASE2").Should().BeFalse();
        Compute(Role(5, new[] { "sage.1.update" })).CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeFalse();
        Compute(Role(5, new[] { "sage.1.read" }), Remove("sage.1.read")).CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeFalse();
        sut.CanUseDatabase(DatabaseKind.Sage, null).Should().BeFalse();
    }

    [Fact]
    public void CanUseDatabase_Pointeuse_NeedsGate1AndRead()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "pointeuse.1.read" }));

        // Act & Assert
        sut.CanUseDatabase(DatabaseKind.Pointeuse, "PTG").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Pointeuse, "AUTRE").Should().BeFalse();
    }

    [Fact]
    public void CanUseDatabase_Administrator_UsesAnyKnownDatabase()
    {
        // Arrange
        var sut = Compute(Role(1));

        // Act & Assert
        sut.CanUseDatabase(DatabaseKind.Sage, "BASE2").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Pointeuse, "PTG").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Sage, "INCONNUE").Should().BeFalse();
    }

    [Fact]
    public void CanConfigureDatabase_AnyAccount_NeedsGate1AndConfigurationPermission()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "sage.1.read" }));

        // Act
        var result = sut.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1");

        // Assert
        result.Should().BeFalse();
        var editor = Compute(Role(5, new[] { "sage.1.read", "sage.1.update", "sage.create" }));
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1").Should().BeTrue();
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Create).Should().BeTrue();
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Delete, "BASE1").Should().BeFalse();
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, null).Should().BeFalse();
        editor.CanConfigureDatabase(DatabaseKind.Pointeuse, DatabaseConfigAction.Create).Should().BeFalse();
        // Sans aucun droit sur les bases, aucune configuration (la porte 1 se déduit des codes SAGE: et POINTEUSE:).
        var noGate = Compute(Role(5, new[] { "shifts.read" }));
        noGate.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1").Should().BeFalse();
        Compute(Role(1)).CanConfigureDatabase(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, "PTG").Should().BeTrue();
    }

    [Fact]
    public void CanUseDatabase_WithoutConfigurationPermission_IsAllowed()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "sage.1.read", "shifts.read" }));

        // Act
        var result = sut.CanUseDatabase(DatabaseKind.Sage, "BASE1");

        // Assert
        result.Should().BeTrue();
        sut.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1").Should().BeFalse();
        sut.Inconsistencies.Should().BeEmpty();
    }

    #endregion

    #region ConvertLegacyList (C4)

    [Fact]
    public void ConvertLegacyList_JoelLikeList_Gives37AdditionsAndNoRemoval()
    {
        // Arrange
        var roleRights = new[] { "sage.1.read", "pointeuse.1.read" }
            .Concat(PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { Processing }))
            .ToList();

        // Act
        var params37 = PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { Parameters });

        // Assert
        params37.Count.Should().Be(37);
        var (additions, removals) = ConvertLegacyList(roleRights, roleRights.Concat(params37).ToList());
        additions.ToHashSet().SetEquals(params37).Should().BeTrue();
        removals.Should().BeEmpty();
        // Appliquer les écarts au rôle redonne exactement la liste d'origine.
        var back = Compute(Role(10034, roleRights.ToArray()), additions.Select(a => new PermissionRow(a, true)).ToArray(), Dbs);
        back.Permissions.SetEquals(roleRights.Concat(params37)).Should().BeTrue();
        back.HasGate(Parameters).Should().BeTrue();
        back.SensitiveRights.Should().Equal(new[] { "users.read", "roles.read", "privileges.read", "db-access.read" });
    }

    [Fact]
    public void ConvertLegacyList_EmptyListOrMarker_RemovesEveryRoleRight()
    {
        // Arrange
        var role = new[] { "shifts.read", "reports.read" };
        var (a1, r1) = ConvertLegacyList(role, Array.Empty<string>());

        // Act
        var (a2, r2) = ConvertLegacyList(role, new[] { PermissionCatalog.EmptyUserMarker });

        // Assert
        a1.Should().BeEmpty();
        a2.Should().BeEmpty();
        r1.Should().Equal(new[] { "reports.read", "shifts.read" });
        r2.Should().Equal(r1);
    }

    [Fact]
    public void ConvertLegacyList_MixedDuplicatedOrUnknownCodes_AreHandled()
    {
        // Act
        var result = ConvertLegacyList(new[] { "shifts.read", "punches.manage" }, new[] { "shifts.read", "shifts.read", "canteen.manage", "INCONNU:X" });

        // Assert
        result.Additions.Should().Equal(new[] { "canteen.manage" });
        result.Removals.Should().Equal(new[] { "punches.manage" });
        result.IgnoredUnknown.Should().Equal(new[] { "INCONNU:X" });
    }

    #endregion

    #region Anomalies connues (C5)

    [Fact]
    public void Compute_NoOrUnknownRole_HasNoRightEvenWithAdditions()
    {
        foreach (var role in new[] { new RoleDefinition(null, false, None), new RoleDefinition(404, false, None) })
        {
            var r = Compute(role, Add("users.read", "shifts.read"));
            r.Permissions.Should().BeEmpty();
            r.Gates.Should().BeEmpty();
            r.Inconsistencies.Should().ContainSingle().Which.Code.Should().Be("ROLE_NOT_FOUND");
        }
    }

    [Fact]
    public void Compute_UnknownPermissions_AreIgnoredAndReported()
    {
        // Act
        var r = Compute(Role(5, new[] { "shifts.read", "FOO:BAR" }), Add("BAZ:QUX", "canteen.manage"));

        // Assert
        (r.Has("FOO:BAR") || r.Has("BAZ:QUX")).Should().BeFalse();
        r.IgnoredPermissions.OrderBy(x => x).SequenceEqual(new[] { "BAZ:QUX", "FOO:BAR" }).Should().BeTrue();
        r.Has("canteen.manage").Should().BeTrue();
    }

    [Fact]
    public void Compute_DuplicatedPermissions_AreMergedSilently()
    {
        // Act
        var r = Compute(Role(5, new[] { "shifts.read", "shifts.read", "proc:shifts" }), Add("canteen.manage", "canteen.manage"));

        // Assert
        r.Permissions.Count.Should().Be(2);
    }

    [Fact]
    public void Compute_RemovingRightTheRoleLacks_HasNoEffectAndIsListed()
    {
        // Arrange
        var role = Role(5, new[] { "shifts.read" });
        var r = Compute(role, Remove("users.read"));

        // Act
        var result = r.Permissions.SetEquals(new[] { "shifts.read" });

        // Assert
        result.Should().BeTrue();
        r.IsCustomized.Should().BeFalse();
        r.IneffectiveDeviations.Should().Equal(new[] { "-users.read" });
    }

    [Fact]
    public void Compute_AddingRightTheRoleHas_HasNoEffectAndIsListed()
    {
        // Act
        var r = Compute(Role(5, new[] { "shifts.read" }), Add("shifts.read"));

        // Assert
        r.IsCustomized.Should().BeFalse();
        r.IneffectiveDeviations.Should().Equal(new[] { "+shifts.read" });
    }

    [Fact]
    public void Compute_SameCodeAddedAndRemoved_IsRemoved()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "shifts.read" }), Add("canteen.manage").Concat(Remove("canteen.manage")).ToArray());

        // Act
        var result = sut.Has("canteen.manage");

        // Assert
        result.Should().BeFalse();
        sut.IneffectiveDeviations.Should().Contain("+canteen.manage");
    }

    #endregion

    #region Équivalence avec le calcul actuel (AuthService.GetUserDetailedPermissionsAsync)

    private sealed class OldWorld
    {
        public readonly InMemoryUnitOfWork Uow = new();
        public readonly AuthServices Auth;

        public OldWorld()
        {
            var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
            Auth = new AuthServices(Uow, new Token(), mapper, new Mail(), new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), new PasswordHasher<Responsable>());
            Uow.Get<BddSage>().Items.AddRange(new[] { new BddSage { Id = 1, NomBd = "BASE1" }, new BddSage { Id = 2, NomBd = "BASE2" } });
            Uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTG" });
        }

        public async Task<HashSet<string>> OldAsync(decimal roleId, string[]? roleRows, int[] gates)
        {
            Uow.Get<Role>().Items.Add(new Role { IdRole = roleId, NomRole = $"R{roleId}" });
            Uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 100 + roleId, IdRole = roleId, Login = "u" });
            foreach (var g in gates) Uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = roleId, IdDroit = g });
            foreach (var c in roleRows ?? Array.Empty<string>()) Uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = roleId, PermissionCode = c, IsGranted = true });
            return (await Auth.GetUserDetailedPermissionsAsync(100 + roleId)).GrantedPermissions.ToHashSet();
        }
    }

    [Fact]
    public async Task Compute_RoleWithDetailRows_MatchesCurrentCalculation()
    {
        // Arrange
        var world = new OldWorld();
        var rows = new[] { "shifts.read", "reports.read", "sage.1.read" };
        var old = await world.OldAsync(5, rows, new[] { Databases, Processing, Reports });
        var now = Compute(Role(5, rows), None, Dbs);

        // Act
        var result = now.Permissions.SetEquals(old);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Compute_Administrator_MatchesCurrentCalculation()
    {
        // Arrange
        var world = new OldWorld();
        var old = await world.OldAsync(1, null, new[] { 1, 2, 3, 4, 5, 6 });
        var now = Compute(Role(1));

        // Act
        var result = now.Permissions.SetEquals(old);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetRoleDetailedPermissionsAsync_RoleWithGatesWithoutRows_MatchesCalculator()
    {
        // Arrange
        // Étape 4b-10 : avant, la lecture actuelle ajoutait la configuration des bases (sage.create, UPDATE, DELETE et pointeuse)
        // que le calculateur n'accorde pas ; l'attendu précédent était « différence = configuration seulement ».
        var world = new OldWorld();
        var gates = new[] { Databases, Processing, Reports };
        var shown = await world.OldAsync(10032, null, gates);
        var now = Compute(Role(10032, null));

        // Act
        var result = shown.SetEquals(now.Permissions);

        // Assert
        result.Should().BeTrue();
        shown.Should().NotContain(c => DatabasePermissionCodes.IsConfiguration(c));
    }

    private sealed class Token : ITokenService
    {
        public string CreateToken(TokenSubject subject) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    #endregion
}
