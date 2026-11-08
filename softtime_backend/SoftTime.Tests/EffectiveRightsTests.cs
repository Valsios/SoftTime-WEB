using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Tests.Http;

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
        var r = Compute(Role(5, SalesRole, Databases, Processing, Reports));
        Assert.True(r.Permissions.SetEquals(SalesRole));
        Assert.True(r.Gates.SetEquals(new[] { Databases, Processing, Reports }));
        Assert.False(r.IsCustomized);
        Assert.Empty(r.Inconsistencies);
        Assert.Empty(r.SensitiveRights);
    }

    [Fact]
    public void Addition_grants_the_right_and_its_gate()
    {
        var r = Compute(Role(5, SalesRole, Databases, Processing, Reports), Add("PARAMS:USERS"));
        Assert.True(r.Has("PARAMS:USERS"));
        Assert.True(r.HasGate(Parameters));
        Assert.True(r.IsCustomized);
        Assert.Equal(new[] { "PARAMS:USERS" }, r.SensitiveRights);
    }

    [Fact]
    public void Removal_takes_the_right_away_and_the_gate_when_nothing_is_left()
    {
        var r = Compute(Role(5, SalesRole, Databases, Processing, Reports), Remove("PROC:SHIFTS", "PROC:PUNCHES"));
        Assert.False(r.Has("PROC:SHIFTS"));
        Assert.False(r.HasGate(Processing));
        Assert.True(r.HasGate(Reports));
        Assert.True(r.IsCustomized);
    }

    [Fact]
    public void Addition_and_removal_together()
    {
        var r = Compute(Role(5, SalesRole, Databases, Processing, Reports), Add("OTHER:CANTEEN").Concat(Remove("REPORTS:REPORTS")).ToArray());
        Assert.True(r.Has("OTHER:CANTEEN"));
        Assert.False(r.Has("REPORTS:REPORTS"));
        Assert.True(r.Gates.SetEquals(new[] { Databases, Processing, Other }));
    }

    [Fact]
    public void Administrator_has_everything_and_is_not_modifiable()
    {
        var withRows = Compute(Role(1, new[] { "REPORTS:REPORTS" }), Remove("PARAMS:USERS", "SAGE:BASE1:READ").Concat(Add("INCONNU:X")).ToArray());
        var plain = Compute(Role(1));
        Assert.True(withRows.IsAdministrator);
        Assert.True(withRows.Permissions.SetEquals(plain.Permissions));
        Assert.True(withRows.Gates.SetEquals(new[] { 1, 2, 3, 4, 5, 6 }));
        Assert.False(withRows.IsCustomized);
        Assert.Equal(SensitiveCodes, withRows.SensitiveRights);
        Assert.Empty(withRows.Inconsistencies);
        Assert.True(withRows.Has("SAGE:BASE2:UPDATE") && withRows.Has("POINTEUSE:CREATE"));
        // Seul le rôle 1 est administrateur : un rôle nommé comme tel n'a rien de spécial.
        Assert.False(Compute(Role(3, new[] { "REPORTS:REPORTS" }, Reports)).IsAdministrator);
    }

    #endregion

    #region Rôle sans lignes (comme 10032) et marqueurs

    [Fact]
    public void Role_with_gates_but_no_rows_gets_the_old_defaults_minus_database_configuration()
    {
        var gates = new[] { Databases, Processing, Other, Traceability, Reports };
        var r = Compute(Role(10032, null, gates));

        var old = PermissionCatalog.BuildDefaultPermissionsList(
            gates.ToHashSet(),
            new[] { new BddSage { NomBd = "BASE1" }, new BddSage { NomBd = "BASE2" } },
            new[] { new BddPointeuse { NomBd = "PTG" } });
        var configuration = new[] { "SAGE:CREATE", "SAGE:BASE1:UPDATE", "SAGE:BASE1:DELETE", "SAGE:BASE2:UPDATE", "SAGE:BASE2:DELETE",
            "POINTEUSE:CREATE", "POINTEUSE:PTG:UPDATE", "POINTEUSE:PTG:DELETE" };
        Assert.True(r.Permissions.SetEquals(old.Except(configuration)));
        Assert.All(configuration, c => Assert.False(r.Has(c)));
        Assert.True(r.Has("SAGE:BASE1:READ") && r.Has("SAGE:BASE2:READ") && r.Has("POINTEUSE:PTG:READ"));
        Assert.True(r.Gates.SetEquals(gates));
        Assert.False(r.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Create));
        Assert.False(r.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1"));
        Assert.True(r.CanUseDatabase(DatabaseKind.Sage, "BASE1"));
        Assert.False(r.CanUseDatabase(DatabaseKind.Sage, "BASE2")); // non cochée dans « Accès bases »
    }

    [Fact]
    public void Database_gate_without_any_database_is_kept()
    {
        var r = Compute(Role(7, null, Databases), None, DatabaseCatalog.Empty);
        Assert.True(r.HasGate(Databases));
        Assert.Empty(r.Permissions);
    }

    [Fact]
    public void Role_marker_means_no_rights_and_no_defaults()
    {
        var r = Compute(Role(8, new[] { PermissionCatalog.EmptyRoleMarker }, Databases, Processing));
        Assert.Empty(r.Permissions);
        Assert.Empty(r.Gates);
    }

    [Fact]
    public void User_marker_removes_every_role_right_and_additions_still_apply()
    {
        var role = Role(5, SalesRole, Databases, Processing, Reports);
        var none = Compute(role, Add(PermissionCatalog.EmptyUserMarker));
        Assert.Empty(none.Permissions);
        Assert.Empty(none.Gates);
        Assert.True(none.IsCustomized);

        var plusOne = Compute(role, Add(PermissionCatalog.EmptyUserMarker, "REPORTS:REPORTS"));
        Assert.True(plusOne.Permissions.SetEquals(new[] { "REPORTS:REPORTS" }));
        Assert.True(plusOne.Gates.SetEquals(new[] { Reports }));
    }

    #endregion

    #region Changement de rôle, portes, droits sensibles

    [Fact]
    public void Role_change_keeps_the_same_deviations_on_the_new_role()
    {
        var user = Add("PARAMS:USERS").Concat(Remove("PROC:SHIFTS")).ToArray();
        var a = Compute(Role(5, new[] { "PROC:SHIFTS", "REPORTS:REPORTS" }, Processing, Reports), user);
        var b = Compute(Role(6, new[] { "PROC:SHIFTS", "OTHER:CANTEEN" }, Processing, Other), user);
        Assert.True(a.Permissions.SetEquals(new[] { "REPORTS:REPORTS", "PARAMS:USERS" }));
        Assert.True(b.Permissions.SetEquals(new[] { "OTHER:CANTEEN", "PARAMS:USERS" }));
    }

    [Fact]
    public void Gates_are_deduced_from_effective_rights_only()
    {
        var r = Compute(Role(9, new[] { "SAGE:BASE1:READ", "PARAMS:ROLES", "PROC:SHIFTS", "OTHER:CANTEEN", "TRACE:AUDIT", "REPORTS:REPORTS" }));
        Assert.True(r.Gates.SetEquals(new[] { 1, 2, 3, 4, 5, 6 }));
        // Les portes de T_DROIT_ROLE ne comptent pas quand le rôle a des lignes détaillées.
        Assert.True(Compute(Role(9, new[] { "REPORTS:REPORTS" }, 1, 2, 3, 4, 5)).Gates.SetEquals(new[] { Reports }));
    }

    [Fact]
    public void Sensitive_rights_lists_the_four_rights_that_change_rights()
    {
        var r = Compute(Role(9, new[] { "PARAMS:DB_ACCESS", "PARAMS:USERS", "PARAMS:CARDPAIE" }), Add("PARAMS:PRIVILEGES", "PARAMS:ROLES"));
        Assert.Equal(new[] { "PARAMS:USERS", "PARAMS:ROLES", "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS" }, r.SensitiveRights);
    }

    #endregion

    #region Incohérence « porte de travail sans accès aux bases »

    [Theory]
    [InlineData(Processing, "PROC:SHIFTS")]
    [InlineData(Other, "OTHER:CANTEEN")]
    [InlineData(Reports, "REPORTS:REPORTS")]
    public void Work_gate_without_database_access_is_reported_but_not_blocked(int gate, string code)
    {
        // Aucune base cochée dans « Accès bases ».
        var none = Compute(Role(5, new[] { code, "SAGE:BASE1:READ" }), None, new DatabaseCatalog(Dbs.Sage, Dbs.Pointeuse, Array.Empty<string>()));
        Assert.Equal(gate, Assert.Single(none.Inconsistencies).Gate);
        Assert.True(none.Has(code));

        // Base cochée mais sans permission de lecture.
        var noRead = Compute(Role(5, new[] { code, "SAGE:BASE2:READ" }));
        Assert.Single(noRead.Inconsistencies);

        // Base cochée et lecture accordée : cohérent.
        Assert.Empty(Compute(Role(5, new[] { code, "SAGE:BASE1:READ" })).Inconsistencies);
    }

    [Fact]
    public void Non_work_gates_and_administrator_are_never_inconsistent()
    {
        var noAccess = new DatabaseCatalog(Dbs.Sage, Dbs.Pointeuse, Array.Empty<string>());
        Assert.Empty(Compute(Role(5, new[] { "PARAMS:USERS", "TRACE:AUDIT" }), None, noAccess).Inconsistencies);
        Assert.Empty(Compute(Role(1), None, noAccess).Inconsistencies);
    }

    [Fact]
    public void Several_work_gates_give_one_inconsistency_each()
    {
        var r = Compute(Role(5, new[] { "PROC:SHIFTS", "REPORTS:REPORTS" }), None, DatabaseCatalog.Empty);
        Assert.Equal(new[] { Processing, Reports }, r.Inconsistencies.Select(i => i.Gate).OrderBy(g => g));
    }

    #endregion

    #region Utiliser / configurer une base (R4)

    [Fact]
    public void Using_a_sage_database_needs_gate_1_checked_database_and_read()
    {
        var ok = Compute(Role(5, new[] { "SAGE:BASE1:READ" }, Databases));
        Assert.True(ok.CanUseDatabase(DatabaseKind.Sage, "BASE1"));
        Assert.True(ok.CanUseDatabase(DatabaseKind.Sage, "base1")); // les noms de base ne tiennent pas compte de la casse

        Assert.False(Compute(Role(5, new[] { "SAGE:BASE2:READ" }, Databases)).CanUseDatabase(DatabaseKind.Sage, "BASE2")); // non cochée
        Assert.False(Compute(Role(5, new[] { "SAGE:BASE1:UPDATE" }, Databases)).CanUseDatabase(DatabaseKind.Sage, "BASE1")); // pas de lecture
        Assert.False(Compute(Role(5, new[] { "SAGE:BASE1:READ" }, Databases), Remove("SAGE:BASE1:READ")).CanUseDatabase(DatabaseKind.Sage, "BASE1"));
        Assert.False(ok.CanUseDatabase(DatabaseKind.Sage, null));
    }

    [Fact]
    public void Pointeuse_use_needs_gate_1_and_read_only_rule_to_be_decided()
    {
        var r = Compute(Role(5, new[] { "POINTEUSE:PTG:READ" }, Databases));
        Assert.True(r.CanUseDatabase(DatabaseKind.Pointeuse, "PTG"));
        Assert.False(r.CanUseDatabase(DatabaseKind.Pointeuse, "AUTRE"));
    }

    [Fact]
    public void Administrator_uses_any_known_database_even_unchecked()
    {
        var admin = Compute(Role(1));
        Assert.True(admin.CanUseDatabase(DatabaseKind.Sage, "BASE2"));
        Assert.True(admin.CanUseDatabase(DatabaseKind.Pointeuse, "PTG"));
        Assert.False(admin.CanUseDatabase(DatabaseKind.Sage, "INCONNUE"));
    }

    [Fact]
    public void Configuring_needs_gate_1_and_a_configuration_permission_for_every_account()
    {
        var reader = Compute(Role(5, new[] { "SAGE:BASE1:READ" }, Databases));
        Assert.False(reader.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1"));

        var editor = Compute(Role(5, new[] { "SAGE:BASE1:READ", "SAGE:BASE1:UPDATE", "SAGE:CREATE" }, Databases));
        Assert.True(editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1"));
        Assert.True(editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Create));
        Assert.False(editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Delete, "BASE1"));
        Assert.False(editor.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, null));
        Assert.False(editor.CanConfigureDatabase(DatabaseKind.Pointeuse, DatabaseConfigAction.Create));

        // Sans aucun droit sur les bases, aucune configuration (la porte 1 se déduit des codes SAGE: et POINTEUSE:).
        var noGate = Compute(Role(5, new[] { "PROC:SHIFTS" }, Processing));
        Assert.False(noGate.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1"));
        Assert.True(Compute(Role(1)).CanConfigureDatabase(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, "PTG"));
    }

    [Fact]
    public void Working_never_requires_configuration()
    {
        var worker = Compute(Role(5, new[] { "SAGE:BASE1:READ", "PROC:SHIFTS" }, Databases, Processing));
        Assert.True(worker.CanUseDatabase(DatabaseKind.Sage, "BASE1"));
        Assert.False(worker.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1"));
        Assert.Empty(worker.Inconsistencies);
    }

    #endregion

    #region ConvertLegacyList (C4)

    [Fact]
    public void Legacy_list_of_a_user_like_joel_gives_37_additions_and_no_removal()
    {
        var roleRights = new[] { "SAGE:BASE1:READ", "POINTEUSE:PTG:READ" }
            .Concat(PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { Processing }, Array.Empty<BddSage>(), Array.Empty<BddPointeuse>()))
            .ToList();
        var params37 = PermissionCatalog.BuildDefaultPermissionsList(new HashSet<int> { Parameters }, Array.Empty<BddSage>(), Array.Empty<BddPointeuse>());
        Assert.Equal(37, params37.Count);

        var (additions, removals) = ConvertLegacyList(roleRights, roleRights.Concat(params37).ToList());
        Assert.True(additions.ToHashSet().SetEquals(params37));
        Assert.Empty(removals);

        // Appliquer les écarts au rôle redonne exactement la liste d'origine.
        var back = Compute(Role(10034, roleRights.ToArray(), Databases, Processing), additions.Select(a => new PermissionRow(a, true)).ToArray(), Dbs);
        Assert.True(back.Permissions.SetEquals(roleRights.Concat(params37)));
        Assert.True(back.HasGate(Parameters));
        Assert.Equal(new[] { "PARAMS:USERS", "PARAMS:ROLES", "PARAMS:PRIVILEGES", "PARAMS:DB_ACCESS" }, back.SensitiveRights);
    }

    [Fact]
    public void Legacy_empty_list_or_marker_removes_every_role_right()
    {
        var role = new[] { "PROC:SHIFTS", "REPORTS:REPORTS" };
        var (a1, r1) = ConvertLegacyList(role, Array.Empty<string>());
        var (a2, r2) = ConvertLegacyList(role, new[] { PermissionCatalog.EmptyUserMarker });
        Assert.Empty(a1);
        Assert.Empty(a2);
        Assert.Equal(new[] { "PROC:SHIFTS", "REPORTS:REPORTS" }, r1);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void Legacy_conversion_handles_mixed_lists_duplicates_and_unknown_codes()
    {
        var result = ConvertLegacyList(new[] { "PROC:SHIFTS", "PROC:PUNCHES" }, new[] { "PROC:SHIFTS", "PROC:SHIFTS", "OTHER:CANTEEN", "INCONNU:X" });
        Assert.Equal(new[] { "OTHER:CANTEEN" }, result.Additions);
        Assert.Equal(new[] { "PROC:PUNCHES" }, result.Removals);
        Assert.Equal(new[] { "INCONNU:X" }, result.IgnoredUnknown);
    }

    #endregion

    #region Anomalies connues (C5)

    [Fact]
    public void User_without_role_or_with_unknown_role_has_no_right_even_with_additions()
    {
        foreach (var role in new[] { new RoleDefinition(null, false, None, Array.Empty<int>()), new RoleDefinition(404, false, None, new[] { 1, 3 }) })
        {
            var r = Compute(role, Add("PARAMS:USERS", "PROC:SHIFTS"));
            Assert.Empty(r.Permissions);
            Assert.Empty(r.Gates);
            Assert.Equal("ROLE_NOT_FOUND", Assert.Single(r.Inconsistencies).Code);
        }
    }

    [Fact]
    public void Unknown_permissions_are_ignored_and_reported_never_accepted()
    {
        var r = Compute(Role(5, new[] { "PROC:SHIFTS", "FOO:BAR" }), Add("BAZ:QUX", "OTHER:CANTEEN"));
        Assert.False(r.Has("FOO:BAR") || r.Has("BAZ:QUX"));
        Assert.True(r.IgnoredPermissions.OrderBy(x => x).SequenceEqual(new[] { "BAZ:QUX", "FOO:BAR" }));
        Assert.True(r.Has("OTHER:CANTEEN"));
    }

    [Fact]
    public void Duplicates_are_merged_silently()
    {
        var r = Compute(Role(5, new[] { "PROC:SHIFTS", "PROC:SHIFTS", "proc:shifts" }), Add("OTHER:CANTEEN", "OTHER:CANTEEN"));
        Assert.Equal(2, r.Permissions.Count);
    }

    [Fact]
    public void Removing_a_right_the_role_lacks_has_no_effect_and_is_listed()
    {
        var role = Role(5, new[] { "PROC:SHIFTS" }, Processing);
        var r = Compute(role, Remove("PARAMS:USERS"));
        Assert.True(r.Permissions.SetEquals(new[] { "PROC:SHIFTS" }));
        Assert.False(r.IsCustomized);
        Assert.Equal(new[] { "-PARAMS:USERS" }, r.IneffectiveDeviations);
    }

    [Fact]
    public void Adding_a_right_the_role_already_has_has_no_effect_and_is_listed()
    {
        var r = Compute(Role(5, new[] { "PROC:SHIFTS" }, Processing), Add("PROC:SHIFTS"));
        Assert.False(r.IsCustomized);
        Assert.Equal(new[] { "+PROC:SHIFTS" }, r.IneffectiveDeviations);
    }

    [Fact]
    public void Same_code_added_and_removed_is_removed()
    {
        var r = Compute(Role(5, new[] { "PROC:SHIFTS" }), Add("OTHER:CANTEEN").Concat(Remove("OTHER:CANTEEN")).ToArray());
        Assert.False(r.Has("OTHER:CANTEEN"));
        Assert.Contains("+OTHER:CANTEEN", r.IneffectiveDeviations);
    }

    #endregion

    #region Équivalence avec le calcul actuel (AuthService.GetUserDetailedPermissionsAsync)

    private sealed class OldWorld
    {
        public readonly SnapshotInMemoryUnitOfWork Uow = new();
        public readonly AuthServices Auth;

        public OldWorld()
        {
            var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
            Auth = new AuthServices(Uow, new Token(), mapper, new Mail(), new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), new PasswordHasher<Responsable>());
            Uow.GetRepo<BddSage>().Items.AddRange(new[] { new BddSage { Id = 1, NomBd = "BASE1" }, new BddSage { Id = 2, NomBd = "BASE2" } });
            Uow.GetRepo<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTG" });
        }

        public async Task<HashSet<string>> OldAsync(decimal roleId, string[]? roleRows, int[] gates)
        {
            Uow.GetRepo<Role>().Items.Add(new Role { IdRole = roleId, NomRole = $"R{roleId}" });
            Uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = 100 + roleId, IdRole = roleId, Login = "u" });
            foreach (var g in gates) Uow.GetRepo<DroitRole>().Items.Add(new DroitRole { IdRole = roleId, IdDroit = g });
            foreach (var c in roleRows ?? Array.Empty<string>()) Uow.GetRepo<RolePermission>().Items.Add(new RolePermission { IdRole = roleId, PermissionCode = c, IsGranted = true });
            return (await Auth.GetUserDetailedPermissionsAsync(100 + roleId)).GrantedPermissions.ToHashSet();
        }
    }

    [Fact]
    public async Task Equivalent_to_the_current_calculation_for_a_role_with_detail_rows()
    {
        var world = new OldWorld();
        var rows = new[] { "PROC:SHIFTS", "REPORTS:REPORTS", "SAGE:BASE1:READ" };
        var old = await world.OldAsync(5, rows, new[] { Databases, Processing, Reports });
        var now = Compute(Role(5, rows, Databases, Processing, Reports), None, new DatabaseCatalog(new[] { "BASE1", "BASE2" }, new[] { "PTG" }, new[] { "BASE1" }));
        Assert.True(now.Permissions.SetEquals(old));
    }

    [Fact]
    public async Task Equivalent_to_the_current_calculation_for_the_administrator()
    {
        var world = new OldWorld();
        var old = await world.OldAsync(1, null, new[] { 1, 2, 3, 4, 5, 6 });
        var now = Compute(Role(1));
        Assert.True(now.Permissions.SetEquals(old));
    }

    [Fact]
    public async Task Role_with_gates_and_no_rows_now_shows_the_same_rights_as_the_calculator()
    {
        // Étape 4b-10 : avant, la lecture actuelle ajoutait la configuration des bases (SAGE:CREATE, UPDATE, DELETE et pointeuse)
        // que le calculateur n'accorde pas ; l'attendu précédent était « différence = configuration seulement ».
        var world = new OldWorld();
        var gates = new[] { Databases, Processing, Reports };
        var shown = await world.OldAsync(10032, null, gates);
        var now = Compute(Role(10032, null, gates));
        Assert.True(shown.SetEquals(now.Permissions));
        Assert.DoesNotContain(shown, c => (c.StartsWith("SAGE:") || c.StartsWith("POINTEUSE:")) && (c.EndsWith(":CREATE") || c.EndsWith(":UPDATE") || c.EndsWith(":DELETE")));
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
