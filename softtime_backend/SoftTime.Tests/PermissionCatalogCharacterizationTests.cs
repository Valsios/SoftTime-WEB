using System.Reflection;

using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;

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

    private static List<string> Defaults(HashSet<int> rights, IEnumerable<T_BDD_SAGE> sages, IEnumerable<T_BDD_POINTEUSE> pointeuses)
        => PermissionCatalog.BuildDefaultPermissionsList(rights, sages, pointeuses);

    // Anciennes entrées, toujours présentes dans AuthService (déléguées au helper) : le résultat doit rester identique.
    private static HashSet<decimal> LegacyGates(IEnumerable<string> codes)
        => (HashSet<decimal>)typeof(AuthService).GetMethod("DeduirePortes", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { codes })!;

    private static List<string> LegacyDefaults(HashSet<int> rights, IEnumerable<T_BDD_SAGE> sages, IEnumerable<T_BDD_POINTEUSE> pointeuses)
        => (List<string>)typeof(AuthService).GetMethod("BuildDefaultPermissionsList", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { rights, sages, pointeuses })!;

    [Fact]
    public void AuthService_entry_points_still_give_the_same_result_as_the_helper()
    {
        var all = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
        var sages = new[] { Sage("A"), Sage("B") };
        var pointeuses = new[] { Pointeuse("P") };
        Assert.Equal(Defaults(all, sages, pointeuses), LegacyDefaults(all, sages, pointeuses));
        Assert.Equal(Defaults(new HashSet<int>(), sages, pointeuses), LegacyDefaults(new HashSet<int>(), sages, pointeuses));
        var codes = new[] { "sage:a:read", "PROC:SHIFTS", "OTHER:CANTEEN", "INCONNU:X" };
        Assert.Equal(Gates(codes).OrderBy(x => x), LegacyGates(codes).OrderBy(x => x));
    }

    [Fact]
    public void Known_static_permissions_are_the_same_set_as_in_AuthService()
    {
        var legacy = (HashSet<string>)typeof(AuthService).GetField("KnownStaticPermissions", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True(legacy.SetEquals(PermissionCatalog.KnownStatic));
        Assert.True(PermissionCatalog.IsKnown("SAGE:X:READ"));
        Assert.True(PermissionCatalog.IsKnown("proc:shifts"));
        Assert.False(PermissionCatalog.IsKnown("N'IMPORTE:QUOI"));
        Assert.False(PermissionCatalog.IsKnown(""));
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

    private static T_BDD_SAGE Sage(string? name) => new() { NOM_BD = name };
    private static T_BDD_POINTEUSE Pointeuse(string? name) => new() { NOM_BD = name };

    private static List<string> Build(int[] rights, string[]? sages = null, string[]? pointeuses = null)
        => Defaults(rights.ToHashSet(), (sages ?? Array.Empty<string>()).Select(Sage), (pointeuses ?? Array.Empty<string>()).Select(Pointeuse));

    #region BuildDefaultPermissionsList

    [Fact]
    public void Defaults_without_gate_and_without_database_are_empty()
        => Assert.Empty(Build(Array.Empty<int>()));

    [Fact]
    public void Defaults_gate_1_without_database_gives_the_two_create_codes()
        => Assert.Equal(new[] { "SAGE:CREATE", "POINTEUSE:CREATE" }, Build(new[] { Rights.Databases }));

    [Fact]
    public void Defaults_gate_1_with_databases_gives_create_then_read_update_delete_per_database()
    {
        var result = Build(new[] { Rights.Databases }, new[] { "A", "B" }, new[] { "P" });
        Assert.Equal(new[]
        {
            "SAGE:CREATE",
            "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE",
            "SAGE:B:READ", "SAGE:B:UPDATE", "SAGE:B:DELETE",
            "POINTEUSE:CREATE",
            "POINTEUSE:P:READ", "POINTEUSE:P:UPDATE", "POINTEUSE:P:DELETE"
        }, result);
    }

    [Fact]
    public void Defaults_without_gate_1_but_with_sage_list_still_gives_sage_codes_and_no_pointeuse()
    {
        var result = Build(new[] { Rights.Processing }, new[] { "A" }, new[] { "P" });
        Assert.Equal(new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE" }.Concat(Proc), result);
    }

    [Fact]
    public void Defaults_skip_blank_database_names()
    {
        var result = Build(new[] { Rights.Databases }, new[] { "A", "", " " }, new[] { "" });
        Assert.Equal(new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE", "POINTEUSE:CREATE" }, result);
    }

    [Fact]
    public void Defaults_single_gates_give_their_fixed_lists()
    {
        Assert.Equal(Params, Build(new[] { Rights.Parameters }));
        Assert.Equal(Proc, Build(new[] { Rights.Processing }));
        Assert.Equal(Other, Build(new[] { Rights.Other }));
        Assert.Equal(new[] { "TRACE:AUDIT" }, Build(new[] { Rights.Traceability }));
        Assert.Equal(new[] { "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING" }, Build(new[] { Rights.Reports }));
    }

    [Fact]
    public void Defaults_all_gates_are_concatenated_in_gate_order()
    {
        var all = new[] { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        var expected = new[] { "SAGE:CREATE", "SAGE:A:READ", "SAGE:A:UPDATE", "SAGE:A:DELETE", "POINTEUSE:CREATE", "POINTEUSE:P:READ", "POINTEUSE:P:UPDATE", "POINTEUSE:P:DELETE" }
            .Concat(Params).Concat(Proc).Concat(Other).Concat(new[] { "TRACE:AUDIT", "REPORTS:REPORTS", "REPORTS:PREDICTIONS", "REPORTS:CLOSING" });
        Assert.Equal(expected, Build(all, new[] { "A" }, new[] { "P" }));
    }

    #endregion

    #region DeduirePortes

    [Fact]
    public void Gates_from_each_prefix()
    {
        Assert.Equal(new decimal[] { 1 }, Gates(new[] { "SAGE:A:READ" }));
        Assert.Equal(new decimal[] { 1 }, Gates(new[] { "POINTEUSE:CREATE" }));
        Assert.Equal(new decimal[] { 2 }, Gates(new[] { "PARAMS:USERS" }));
        Assert.Equal(new decimal[] { 3 }, Gates(new[] { "PROC:SHIFTS" }));
        Assert.Equal(new decimal[] { 4 }, Gates(new[] { "OTHER:CANTEEN" }));
        Assert.Equal(new decimal[] { 5 }, Gates(new[] { "TRACE:AUDIT" }));
        Assert.Equal(new decimal[] { 6 }, Gates(new[] { "REPORTS:REPORTS" }));
        Assert.Equal(new decimal[] { 6 }, Gates(new[] { "REPORTS:PREDICTIONS" }));
        Assert.True(PermissionCatalog.IsKnown("REPORTS:PREDICTIONS"));
    }

    [Fact]
    public void Gates_ignore_case_unknown_prefixes_and_duplicates()
    {
        Assert.Equal(new decimal[] { 1, 3 }.OrderBy(x => x), Gates(new[] { "sage:a:read", "proc:shifts", "PROC:SHIFTS", "INCONNU:X", "" }).OrderBy(x => x));
        Assert.Empty(Gates(Array.Empty<string>()));
        Assert.Empty(Gates(new[] { "INCONNU:X", "__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__" }));
    }

    [Fact]
    public void Gates_from_a_full_default_list_give_all_six()
    {
        var all = new[] { Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports };
        Assert.Equal(new decimal[] { 1, 2, 3, 4, 5, 6 }, Gates(Build(all, new[] { "A" }, new[] { "P" })).OrderBy(x => x));
    }

    #endregion
}
