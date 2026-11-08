using System.Reflection;

using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// Tests de caractérisation de DeduirePortes et BuildDefaultPermissionsList : ils figent le résultat d'AVANT l'extraction
/// (écrits et exécutés contre les méthodes privées d'AuthService), puis prouvent que le helper partagé donne le même résultat.
/// </summary>
public class PermissionCatalogCharacterizationTests
{
    // Les attentes ci-dessous ont été écrites et exécutées contre les méthodes privées d'AuthService AVANT l'extraction ;
    // elles s'exécutent maintenant contre le helper partagé (mêmes valeurs attendues).
    private static HashSet<decimal> Gates(IEnumerable<string> codes) => PermissionCatalog.DeduirePortes(codes);

    private static List<string> Defaults(HashSet<int> rights, IEnumerable<BddSage> sages, IEnumerable<BddPointeuse> pointeuses)
        => PermissionCatalog.BuildDefaultPermissionsList(rights, sages, pointeuses);

    // Anciennes entrées, toujours présentes dans AuthService (déléguées au helper) : le résultat doit rester identique.
    private static HashSet<decimal> LegacyGates(IEnumerable<string> codes)
        => (HashSet<decimal>)typeof(RolePermissionService).GetMethod("DeduirePortes", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { codes })!;

    private static List<string> LegacyDefaults(HashSet<int> rights, IEnumerable<BddSage> sages, IEnumerable<BddPointeuse> pointeuses)
        => (List<string>)typeof(UserPermissionService).GetMethod("BuildDefaultPermissionsList", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { rights, sages, pointeuses })!;

    [Fact]
    public void AuthService_entry_points_still_give_the_same_result_as_the_helper()
    {
        var all = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
        var sages = new[] { Sage("A"), Sage("B") };
        var pointeuses = new[] { Pointeuse("P") };
        LegacyDefaults(all, sages, pointeuses).Should().Equal(Defaults(all, sages, pointeuses));
        LegacyDefaults(new HashSet<int>(), sages, pointeuses).Should().Equal(Defaults(new HashSet<int>(), sages, pointeuses));
        var codes = new[] { "sage:a:read", "PROC:SHIFTS", "OTHER:CANTEEN", "INCONNU:X" };
        LegacyGates(codes).OrderBy(x => x).Should().Equal(Gates(codes).OrderBy(x => x));
    }

    [Fact]
    public void Known_static_permissions_are_the_same_set_as_in_AuthService()
    {
        var legacy = (HashSet<string>)typeof(RolePermissionService).GetField("KnownStaticPermissions", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        legacy.SetEquals(PermissionCatalog.KnownStatic).Should().BeTrue();
        PermissionCatalog.IsKnown("SAGE:X:READ").Should().BeTrue();
        PermissionCatalog.IsKnown("proc:shifts").Should().BeTrue();
        PermissionCatalog.IsKnown("N'IMPORTE:QUOI").Should().BeFalse();
        PermissionCatalog.IsKnown("").Should().BeFalse();
    }

    private static readonly string[] Params =
    {
        "PARAMS:USERS", "PARAMS:USERS:CREATE", "PARAMS:USERS:UPDATE", "PARAMS:USERS:DELETE",
        "PARAMS:ROLES", "PARAMS:ROLES:CREATE", "PARAMS:ROLES:UPDATE", "PARAMS:ROLES:DELETE",
        "PARAMS:PRIVILEGES",
        "PARAMS:DB_ACCESS",
        "PARAMS:CLOCK_PARAMS",
        "PARAMS:CORRESPONDENCE",
        "PARAMS:CARDPAIE", "PARAMS:CARDPAIE:CREATE", "PARAMS:CARDPAIE:UPDATE", "PARAMS:CARDPAIE:DELETE",
        "PARAMS:CATEGORIES", "PARAMS:CATEGORIES:CREATE", "PARAMS:CATEGORIES:UPDATE", "PARAMS:CATEGORIES:DELETE",
        "PARAMS:AFFECTATIONS", "PARAMS:AFFECTATIONS:CREATE", "PARAMS:AFFECTATIONS:UPDATE", "PARAMS:AFFECTATIONS:DELETE",
        "PARAMS:TOLERANCES", "PARAMS:TOLERANCES:CREATE", "PARAMS:TOLERANCES:UPDATE",
        "PARAMS:HOLIDAYS", "PARAMS:HOLIDAYS:CREATE", "PARAMS:HOLIDAYS:UPDATE", "PARAMS:HOLIDAYS:DELETE",
        "PARAMS:MAJORATIONS", "PARAMS:MAJORATIONS:UPDATE",
        "PARAMS:CODE_CONSTANTES",
        "PARAMS:ABSENCE_CODES", "PARAMS:ABSENCE_CODES:CREATE", "PARAMS:ABSENCE_CODES:UPDATE"
    };

    private static readonly string[] Proc =
    {
        "PROC:SHIFTS", "PROC:SHIFTS:CREATE", "PROC:SHIFTS:UPDATE", "PROC:SHIFTS:DELETE",
        "PROC:EMPLOYEE_PLANNING", "PROC:EMPLOYEE_PLANNING:CREATE", "PROC:EMPLOYEE_PLANNING:UPDATE", "PROC:EMPLOYEE_PLANNING:DELETE",
        "PROC:PUNCHES",
        "PROC:ANOMALIES", "PROC:ANOMALIES:DELETE",
        "PROC:CORRECTED_HOURS", "PROC:CORRECTED_HOURS:CREATE", "PROC:CORRECTED_HOURS:UPDATE", "PROC:CORRECTED_HOURS:DELETE",
        "PROC:WEEKLY_VALIDATION",
        "PROC:OVERTIME"
    };

    private static readonly string[] Other = { "OTHER:CANTEEN", "OTHER:TRAVEL_EXPENSES" };

    private static BddSage Sage(string? name) => new() { NomBd = name };
    private static BddPointeuse Pointeuse(string? name) => new() { NomBd = name };

    private static List<string> Build(int[] rights, string[]? sages = null, string[]? pointeuses = null)
        => Defaults(rights.ToHashSet(), (sages ?? Array.Empty<string>()).Select(Sage), (pointeuses ?? Array.Empty<string>()).Select(Pointeuse));

    #region BuildDefaultPermissionsList

    [Fact]
    public void Defaults_without_gate_and_without_database_are_empty()
        => Build(Array.Empty<int>()).Should().BeEmpty();

    [Fact]
    public void Defaults_gate_1_without_database_gives_the_two_create_codes()
        => Build(new[] { Rights.Databases }).Should().Equal(new[] { "SAGE:CREATE", "POINTEUSE:CREATE" });

    [Fact]
    public void Defaults_gate_1_with_databases_gives_create_then_read_update_delete_per_database()
    {
        var result = Build(new[] { Rights.Databases }, new[] { "A", "B" }, new[] { "P" });
        result.Should().Equal(new[]
        {
            "SAGE:CREATE",
            "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE",
            "SAGE:B:READ", "SAGE:B:UPDATE", "SAGE:B:DELETE",
            "POINTEUSE:CREATE",
            "POINTEUSE:P:READ", "POINTEUSE:P:UPDATE", "POINTEUSE:P:DELETE"
        });
    }

    [Fact]
    public void Defaults_without_gate_1_but_with_sage_list_still_gives_sage_codes_and_no_pointeuse()
    {
        var result = Build(new[] { Rights.Processing }, new[] { "A" }, new[] { "P" });
        result.Should().Equal(new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE" }.Concat(Proc));
    }

    [Fact]
    public void Defaults_skip_blank_database_names()
    {
        var result = Build(new[] { Rights.Databases }, new[] { "A", "", " " }, new[] { "" });
        result.Should().Equal(new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE", "POINTEUSE:CREATE" });
    }

    [Fact]
    public void Defaults_single_gates_give_their_fixed_lists()
    {
        Build(new[] { Rights.Parameters }).Should().Equal(Params);
        Build(new[] { Rights.Processing }).Should().Equal(Proc);
        Build(new[] { Rights.Other }).Should().Equal(Other);
        Build(new[] { Rights.Traceability }).Should().Equal(new[] { "TRACE:AUDIT" });
        Build(new[] { Rights.Reports }).Should().Equal(new[] { "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING" });
    }

    [Fact]
    public void Defaults_all_gates_are_concatenated_in_gate_order()
    {
        var all = new[] { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        var expected = new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE", "POINTEUSE:CREATE", "POINTEUSE:P:READ", "POINTEUSE:P:UPDATE", "POINTEUSE:P:DELETE" }
            .Concat(Params).Concat(Proc).Concat(Other).Concat(new[] { "TRACE:AUDIT", "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING" });
        Build(all, new[] { "A" }, new[] { "P" }).Should().Equal(expected);
    }

    #endregion

    #region DeduirePortes

    [Fact]
    public void Gates_from_each_prefix()
    {
        Gates(new[] { "SAGE:A:READ" }).Should().Equal(new decimal[] { 1 });
        Gates(new[] { "POINTEUSE:CREATE" }).Should().Equal(new decimal[] { 1 });
        Gates(new[] { "PARAMS:USERS" }).Should().Equal(new decimal[] { 2 });
        Gates(new[] { "PROC:SHIFTS" }).Should().Equal(new decimal[] { 3 });
        Gates(new[] { "OTHER:CANTEEN" }).Should().Equal(new decimal[] { 4 });
        Gates(new[] { "TRACE:AUDIT" }).Should().Equal(new decimal[] { 5 });
        Gates(new[] { "REPORTS:REPORTS" }).Should().Equal(new decimal[] { 6 });
        Gates(new[] { "REPORTS:PREDICTIONS" }).Should().Equal(new decimal[] { 6 });
        PermissionCatalog.IsKnown("REPORTS:PREDICTIONS").Should().BeTrue();
    }

    [Fact]
    public void Gates_ignore_case_unknown_prefixes_and_duplicates()
    {
        Gates(new[] { "sage:a:read", "proc:shifts", "PROC:SHIFTS", "INCONNU:X", "" }).OrderBy(x => x).Should().Equal(new decimal[] { 1, 3 }.OrderBy(x => x));
        Gates(Array.Empty<string>()).Should().BeEmpty();
        Gates(new[] { "INCONNU:X", "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__" }).Should().BeEmpty();
    }

    [Fact]
    public void Gates_from_a_full_default_list_give_all_six()
    {
        var all = new[] { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        Gates(Build(all, new[] { "A" }, new[] { "P" })).OrderBy(x => x).Should().Equal(new decimal[] { 1, 2, 3, 4, 5, 6 });
    }

    #endregion
}
