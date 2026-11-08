using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
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

    private static readonly DatabaseCatalog Dbs = new(new[] { "BASE1", "BASE2" }, new[] { "PTG" }, new[] { "BASE1" });

    private static RoleDefinition Role(decimal id, string[]? codes = null, params int[] gates)
        => new(id, true, (codes ?? Array.Empty<string>()).Select(c => new PermissionRow(c)).ToList(), gates);

    private static PermissionRow[] Add(params string[] codes) => codes.Select(c => new PermissionRow(c, true)).ToArray();
    private static PermissionRow[] Remove(params string[] codes) => codes.Select(c => new PermissionRow(c, false)).ToArray();
    private static PermissionRow[] None => Array.Empty<PermissionRow>();

    private static EffectiveRights Compute(RoleDefinition role, PermissionRow[]? user = null, DatabaseCatalog? dbs = null)
        => ComputeEffectiveRights(role, user ?? None, dbs ?? Dbs);

    private static readonly string[] SalesRole = { "PROC:SHIFTS", "PROC:PUNCHES", "REPORTS:REPORTS", "SAGE:BASE1:READ" };

    #region Matrice de base

    [Fact]
    public void Role_only_gives_exactly_the_role_rights_and_deduced_gates()
    {
        // Arrange
        var r = Compute(Role(5, SalesRole, Databases, Processing, Reports));

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
    public void Addition_grants_the_right_and_its_gate()
    {
        // Arrange
        var sut = Compute(Role(5, SalesRole, Databases, Processing, Reports), Add("PARAMS:USERS"));

        // Act
        var result = sut.Has("PARAMS:USERS");

        // Assert
        result.Should().BeTrue();
        sut.HasGate(Parameters).Should().BeTrue();
        sut.IsCustomized.Should().BeTrue();
        sut.SensitiveRights.Should().Equal(new[] { "PARAMS:USERS" });
    }

    [Fact]
    public void Removal_takes_the_right_away_and_the_gate_when_nothing_is_left()
    {
        // Arrange
        var sut = Compute(Role(5, SalesRole, Databases, Processing, Reports), Remove("PROC:SHIFTS", "PROC:PUNCHES"));

        // Act
        var result = sut.Has("PROC:SHIFTS");

        // Assert
        result.Should().BeFalse();
        sut.HasGate(Processing).Should().BeFalse();
        sut.HasGate(Reports).Should().BeTrue();
        sut.IsCustomized.Should().BeTrue();
    }

    [Fact]
    public void Addition_and_removal_together()
    {
        // Arrange
        var sut = Compute(Role(5, SalesRole, Databases, Processing, Reports), Add("OTHER:CANTEEN").Concat(Remove("REPORTS:REPORTS")).ToArray());

        // Act & Assert
        sut.Has("OTHER:CANTEEN").Should().BeTrue();
        sut.Has("REPORTS:REPORTS").Should().BeFalse();
        sut.Gates.SetEquals(new[] { Databases, Processing, Other }).Should().BeTrue();
    }

    [Fact]
    public void Administrator_has_everything_and_is_not_modifiable()
    {
        // Arrange
        var withRows = Compute(Role(1, new[] { "REPORTS:REPORTS" }), Remove("PARAMS:USERS", "SAGE:BASE1:READ").Concat(Add("INCONNU:X")).ToArray());

        // Act
        var plain = Compute(Role(1));

        // Assert
        withRows.IsAdministrator.Should().BeTrue();
        withRows.Permissions.SetEquals(plain.Permissions).Should().BeTrue();
        withRows.Gates.SetEquals(new[] { 1, 2, 3, 4, 5, 6 }).Should().BeTrue();
        withRows.IsCustomized.Should().BeFalse();
        withRows.SensitiveRights.Should().Equal(SensitiveCodes);
        withRows.Inconsistencies.Should().BeEmpty();
        (withRows.Has("SAGE:BASE2:UPDATE") && withRows.Has("POINTEUSE:CREATE")).Should().BeTrue();
        // Seul le rôle 1 est administrateur : un rôle nommé comme tel n'a rien de spécial.
        Compute(Role(3, new[] { "REPORTS:REPORTS" }, Reports)).IsAdministrator.Should().BeFalse();
    }

    #endregion

    #region Rôle sans lignes (comme 10032) et marqueurs

    [Fact]
    public void Role_with_gates_but_no_rows_gets_the_old_defaults_minus_database_configuration()
    {
        // Arrange
        var gates = new[] { Databases, Processing, Other, Traceability, Reports };
        var r = Compute(Role(10032, null, gates));
        var old = PermissionCatalog.BuildDefaultPermissionsList(
            gates.ToHashSet(),
            new[] { new BddSage { NomBd = "BASE1" }, new BddSage { NomBd = "BASE2" } },
            new[] { new BddPointeuse { NomBd = "PTG" } });
        var configuration = new[] { "SAGE:CREATE", "SAGE:BASE1:UPDATE", "SAGE:BASE1:DELETE", "SAGE:BASE2:UPDATE", "SAGE:BASE2:DELETE",
            "POINTEUSE:CREATE", "POINTEUSE:PTG:UPDATE", "POINTEUSE:PTG:DELETE" };

        // Act
        var result = r.Permissions.SetEquals(old.Except(configuration));

        // Assert
        result.Should().BeTrue();
        configuration.Should().AllSatisfy(c => r.Has(c).Should().BeFalse());
        (r.Has("SAGE:BASE1:READ") && r.Has("SAGE:BASE2:READ") && r.Has("POINTEUSE:PTG:READ")).Should().BeTrue();
        r.Gates.SetEquals(gates).Should().BeTrue();
        r.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Create).Should().BeFalse();
        r.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1").Should().BeFalse();
        r.CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeTrue();
        r.CanUseDatabase(DatabaseKind.Sage, "BASE2").Should().BeFalse();
    }

    [Fact]
    public void Database_gate_without_any_database_is_kept()
    {
        // Arrange
        var sut = Compute(Role(7, null, Databases), None, DatabaseCatalog.Empty);

        // Act
        var result = sut.HasGate(Databases);

        // Assert
        result.Should().BeTrue();
        sut.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void Role_marker_means_no_rights_and_no_defaults()
    {
        // Act
        var r = Compute(Role(8, new[] { PermissionCatalog.EmptyRoleMarker }, Databases, Processing));

        // Assert
        r.Permissions.Should().BeEmpty();
        r.Gates.Should().BeEmpty();
    }

    [Fact]
    public void User_marker_removes_every_role_right_and_additions_still_apply()
    {
        // Arrange
        var role = Role(5, SalesRole, Databases, Processing, Reports);

        // Act
        var none = Compute(role, Add(PermissionCatalog.EmptyUserMarker));

        // Assert
        none.Permissions.Should().BeEmpty();
        none.Gates.Should().BeEmpty();
        none.IsCustomized.Should().BeTrue();
        var plusOne = Compute(role, Add(PermissionCatalog.EmptyUserMarker, "REPORTS:REPORTS"));
        plusOne.Permissions.SetEquals(new[] { "REPORTS:REPORTS" }).Should().BeTrue();
        plusOne.Gates.SetEquals(new[] { Reports }).Should().BeTrue();
    }

    #endregion

    #region Changement de rôle, portes, droits sensibles

    [Fact]
    public void Role_change_keeps_the_same_deviations_on_the_new_role()
    {
        // Arrange
        var user = Add("PARAMS:USERS").Concat(Remove("PROC:SHIFTS")).ToArray();
        var a = Compute(Role(5, new[] { "PROC:SHIFTS", "REPORTS:REPORTS" }, Processing, Reports), user);
        var b = Compute(Role(6, new[] { "PROC:SHIFTS", "OTHER:CANTEEN" }, Processing, Other), user);

        // Act
        var result = a.Permissions.SetEquals(new[] { "REPORTS:REPORTS", "PARAMS:USERS" });

        // Assert
        result.Should().BeTrue();
        b.Permissions.SetEquals(new[] { "OTHER:CANTEEN", "PARAMS:USERS" }).Should().BeTrue();
    }

    [Fact]
    public void Gates_are_deduced_from_effective_rights_only()
    {
        // Arrange
        var r = Compute(Role(9, new[] { "SAGE:BASE1:READ", "PARAMS:ROLES", "PROC:SHIFTS", "OTHER:CANTEEN", "TRACE:AUDIT", "REPORTS:REPORTS" }));

        // Act
        var result = r.Gates.SetEquals(new[] { 1, 2, 3, 4, 5, 6 });

        // Assert
        result.Should().BeTrue();
        // Les portes de T_DROIT_ROLE ne comptent pas quand le rôle a des lignes détaillées.
        Compute(Role(9, new[] { "REPORTS:REPORTS" }, 1, 2, 3, 4, 5)).Gates.SetEquals(new[] { Reports }).Should().BeTrue();
    }

    [Fact]
    public void Sensitive_rights_lists_the_four_rights_that_change_rights()
    {
        // Act
        var r = Compute(Role(9, new[] { "PARAMS:DB_ACCESS", "PARAMS:USERS", "PARAMS:CARDPAIE" }), Add("PARAMS:PRIVILEGES", "PARAMS:ROLES"));

        // Assert
        r.SensitiveRights.Should().Equal(new[] { "PARAMS:USERS", "PARAMS:ROLES", "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS" });
    }

    #endregion

    #region Incohérence « porte de travail sans accès aux bases »

    [Theory]
    [InlineData(Processing, "PROC:SHIFTS")]
    [InlineData(Other, "OTHER:CANTEEN")]
    [InlineData(Reports, "REPORTS:REPORTS")]
    public void Work_gate_without_database_access_is_reported_but_not_blocked(int gate, string code)
    {
        // Act
        // Aucune base cochée dans « Accès bases ».
        var none = Compute(Role(5, new[] { code, "SAGE:BASE1:READ" }), None, new DatabaseCatalog(Dbs.Sage, Dbs.Pointeuse, Array.Empty<string>()));

        // Assert
        none.Inconsistencies.Should().ContainSingle().Which.Gate.Should().Be(gate);
        none.Has(code).Should().BeTrue();
        // Base cochée mais sans permission de lecture.
        var noRead = Compute(Role(5, new[] { code, "SAGE:BASE2:READ" }));
        noRead.Inconsistencies.Should().ContainSingle();
        // Base cochée et lecture accordée : cohérent.
        Compute(Role(5, new[] { code, "SAGE:BASE1:READ" })).Inconsistencies.Should().BeEmpty();
    }

    [Fact]
    public void Non_work_gates_and_administrator_are_never_inconsistent()
    {
        var noAccess = new DatabaseCatalog(Dbs.Sage, Dbs.Pointeuse, Array.Empty<string>());
        Compute(Role(5, new[] { "PARAMS:USERS", "TRACE:AUDIT" }), None, noAccess).Inconsistencies.Should().BeEmpty();
        Compute(Role(1), None, noAccess).Inconsistencies.Should().BeEmpty();
    }

    [Fact]
    public void Several_work_gates_give_one_inconsistency_each()
    {
        // Arrange
        var r = Compute(Role(5, new[] { "PROC:SHIFTS", "REPORTS:REPORTS" }), None, DatabaseCatalog.Empty);

        // Act
        var result = r.Inconsistencies.Select(i => i.Gate).OrderBy(g => g);

        // Assert
        result.Should().Equal(new[] { Processing, Reports });
    }

    #endregion

    #region Utiliser / configurer une base (R4)

    [Fact]
    public void Using_a_sage_database_needs_gate_1_checked_database_and_read()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "SAGE:BASE1:READ" }, Databases));

        // Act & Assert
        sut.CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Sage, "base1").Should().BeTrue();
        Compute(Role(5, new[] { "SAGE:BASE2:READ" }, Databases)).CanUseDatabase(DatabaseKind.Sage, "BASE2").Should().BeFalse();
        Compute(Role(5, new[] { "SAGE:BASE1:UPDATE" }, Databases)).CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeFalse();
        Compute(Role(5, new[] { "SAGE:BASE1:READ" }, Databases), Remove("SAGE:BASE1:READ")).CanUseDatabase(DatabaseKind.Sage, "BASE1").Should().BeFalse();
        sut.CanUseDatabase(DatabaseKind.Sage, null).Should().BeFalse();
    }

    [Fact]
    public void Pointeuse_use_needs_gate_1_and_read_only_rule_to_be_decided()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "POINTEUSE:PTG:READ" }, Databases));

        // Act & Assert
        sut.CanUseDatabase(DatabaseKind.Pointeuse, "PTG").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Pointeuse, "AUTRE").Should().BeFalse();
    }

    [Fact]
    public void Administrator_uses_any_known_database_even_unchecked()
    {
        // Arrange
        var sut = Compute(Role(1));

        // Act & Assert
        sut.CanUseDatabase(DatabaseKind.Sage, "BASE2").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Pointeuse, "PTG").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Sage, "INCONNUE").Should().BeFalse();
    }

    [Fact]
    public void Configuring_needs_gate_1_and_a_configuration_permission_for_every_account()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "SAGE:BASE1:READ" }, Databases));

        // Act
        var result = sut.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1");

        // Assert
        result.Should().BeFalse();
        var editor = Compute(Role(5, new[] { "SAGE:BASE1:READ", "SAGE:BASE1:UPDATE", "SAGE:CREATE" }, Databases));
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1").Should().BeTrue();
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Create).Should().BeTrue();
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Delete, "BASE1").Should().BeFalse();
        editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, null).Should().BeFalse();
        editor.CanConfigureDatabase(DatabaseKind.Pointeuse, DatabaseConfigAction.Create).Should().BeFalse();
        // Sans aucun droit sur les bases, aucune configuration (la porte 1 se déduit des codes SAGE: et POINTEUSE:).
        var noGate = Compute(Role(5, new[] { "PROC:SHIFTS" }, Processing));
        noGate.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1").Should().BeFalse();
        Compute(Role(1)).CanConfigureDatabase(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, "PTG").Should().BeTrue();
    }

    [Fact]
    public void Working_never_requires_configuration()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "SAGE:BASE1:READ", "PROC:SHIFTS" }, Databases, Processing));

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
    public void Legacy_list_of_a_user_like_joel_gives_37_additions_and_no_removal()
    {
        // Arrange
        var roleRights = new[] { "SAGE:BASE1:READ", "POINTEUSE:PTG:READ" }
            .Concat(PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { Processing }, Array.Empty<BddSage>(), Array.Empty<BddPointeuse>()))
            .ToList();

        // Act
        var params37 = PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { Parameters }, Array.Empty<BddSage>(), Array.Empty<BddPointeuse>());

        // Assert
        params37.Count.Should().Be(37);
        var (additions, removals) = ConvertLegacyList(roleRights, roleRights.Concat(params37).ToList());
        additions.ToHashSet().SetEquals(params37).Should().BeTrue();
        removals.Should().BeEmpty();
        // Appliquer les écarts au rôle redonne exactement la liste d'origine.
        var back = Compute(Role(10034, roleRights.ToArray(), Databases, Processing), additions.Select(a => new PermissionRow(a, true)).ToArray(), Dbs);
        back.Permissions.SetEquals(roleRights.Concat(params37)).Should().BeTrue();
        back.HasGate(Parameters).Should().BeTrue();
        back.SensitiveRights.Should().Equal(new[] { "PARAMS:USERS", "PARAMS:ROLES", "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS" });
    }

    [Fact]
    public void Legacy_empty_list_or_marker_removes_every_role_right()
    {
        // Arrange
        var role = new[] { "PROC:SHIFTS", "REPORTS:REPORTS" };
        var (a1, r1) = ConvertLegacyList(role, Array.Empty<string>());

        // Act
        var (a2, r2) = ConvertLegacyList(role, new[] { PermissionCatalog.EmptyUserMarker });

        // Assert
        a1.Should().BeEmpty();
        a2.Should().BeEmpty();
        r1.Should().Equal(new[] { "PROC:SHIFTS", "REPORTS:REPORTS" });
        r2.Should().Equal(r1);
    }

    [Fact]
    public void Legacy_conversion_handles_mixed_lists_duplicates_and_unknown_codes()
    {
        // Act
        var result = ConvertLegacyList(new[] { "PROC:SHIFTS", "PROC:PUNCHES" }, new[] { "PROC:SHIFTS", "PROC:SHIFTS", "OTHER:CANTEEN", "INCONNU:X" });

        // Assert
        result.Additions.Should().Equal(new[] { "OTHER:CANTEEN" });
        result.Removals.Should().Equal(new[] { "PROC:PUNCHES" });
        result.IgnoredUnknown.Should().Equal(new[] { "INCONNU:X" });
    }

    #endregion

    #region Anomalies connues (C5)

    [Fact]
    public void User_without_role_or_with_unknown_role_has_no_right_even_with_additions()
    {
        foreach (var role in new[] { new RoleDefinition(null, false, None, Array.Empty<int>()), new RoleDefinition(404, false, None, new[] { 1, 3 }) })
        {
            var r = Compute(role, Add("PARAMS:USERS", "PROC:SHIFTS"));
            r.Permissions.Should().BeEmpty();
            r.Gates.Should().BeEmpty();
            r.Inconsistencies.Should().ContainSingle().Which.Code.Should().Be("ROLE_NOT_FOUND");
        }
    }

    [Fact]
    public void Unknown_permissions_are_ignored_and_reported_never_accepted()
    {
        // Act
        var r = Compute(Role(5, new[] { "PROC:SHIFTS", "FOO:BAR" }), Add("BAZ:QUX", "OTHER:CANTEEN"));

        // Assert
        (r.Has("FOO:BAR") || r.Has("BAZ:QUX")).Should().BeFalse();
        r.IgnoredPermissions.OrderBy(x => x).SequenceEqual(new[] { "BAZ:QUX", "FOO:BAR" }).Should().BeTrue();
        r.Has("OTHER:CANTEEN").Should().BeTrue();
    }

    [Fact]
    public void Duplicates_are_merged_silently()
    {
        // Act
        var r = Compute(Role(5, new[] { "PROC:SHIFTS", "PROC:SHIFTS", "proc:shifts" }), Add("OTHER:CANTEEN", "OTHER:CANTEEN"));

        // Assert
        r.Permissions.Count.Should().Be(2);
    }

    [Fact]
    public void Removing_a_right_the_role_lacks_has_no_effect_and_is_listed()
    {
        // Arrange
        var role = Role(5, new[] { "PROC:SHIFTS" }, Processing);
        var r = Compute(role, Remove("PARAMS:USERS"));

        // Act
        var result = r.Permissions.SetEquals(new[] { "PROC:SHIFTS" });

        // Assert
        result.Should().BeTrue();
        r.IsCustomized.Should().BeFalse();
        r.IneffectiveDeviations.Should().Equal(new[] { "-PARAMS:USERS" });
    }

    [Fact]
    public void Adding_a_right_the_role_already_has_has_no_effect_and_is_listed()
    {
        // Act
        var r = Compute(Role(5, new[] { "PROC:SHIFTS" }, Processing), Add("PROC:SHIFTS"));

        // Assert
        r.IsCustomized.Should().BeFalse();
        r.IneffectiveDeviations.Should().Equal(new[] { "+PROC:SHIFTS" });
    }

    [Fact]
    public void Same_code_added_and_removed_is_removed()
    {
        // Arrange
        var sut = Compute(Role(5, new[] { "PROC:SHIFTS" }), Add("OTHER:CANTEEN").Concat(Remove("OTHER:CANTEEN")).ToArray());

        // Act
        var result = sut.Has("OTHER:CANTEEN");

        // Assert
        result.Should().BeFalse();
        sut.IneffectiveDeviations.Should().Contain("+OTHER:CANTEEN");
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
    public async Task Equivalent_to_the_current_calculation_for_a_role_with_detail_rows()
    {
        // Arrange
        var world = new OldWorld();
        var rows = new[] { "PROC:SHIFTS", "REPORTS:REPORTS", "SAGE:BASE1:READ" };
        var old = await world.OldAsync(5, rows, new[] { Databases, Processing, Reports });
        var now = Compute(Role(5, rows, Databases, Processing, Reports), None, new DatabaseCatalog(new[] { "BASE1", "BASE2" }, new[] { "PTG" }, new[] { "BASE1" }));

        // Act
        var result = now.Permissions.SetEquals(old);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Equivalent_to_the_current_calculation_for_the_administrator()
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
    public async Task Role_with_gates_and_no_rows_now_shows_the_same_rights_as_the_calculator()
    {
        // Arrange
        // Étape 4b-10 : avant, la lecture actuelle ajoutait la configuration des bases (SAGE:CREATE, UPDATE, DELETE et pointeuse)
        // que le calculateur n'accorde pas ; l'attendu précédent était « différence = configuration seulement ».
        var world = new OldWorld();
        var gates = new[] { Databases, Processing, Reports };
        var shown = await world.OldAsync(10032, null, gates);
        var now = Compute(Role(10032, null, gates));

        // Act
        var result = shown.SetEquals(now.Permissions);

        // Assert
        result.Should().BeTrue();
        shown.Should().NotContain(c => (c.StartsWith("SAGE:") || c.StartsWith("POINTEUSE:")) && (c.EndsWith(":CREATE") || c.EndsWith(":UPDATE") || c.EndsWith(":DELETE")));
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
