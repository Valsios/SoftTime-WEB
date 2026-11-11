namespace SoftTime.Application.Security;

/// <summary>
/// Codes des permissions fines, à utiliser avec <c>[HasPermission]</c> : jamais de code écrit en dur dans un contrôleur.
/// Chaque constante doit être connue de <see cref="PermissionCatalog"/> (vérifié par PermissionsConstantsTests).
/// </summary>
public static class Permissions
{
    public static class AbsenceCodes
    {
        public const string Read = "PARAMS:ABSENCE_CODES";
        public const string Create = "PARAMS:ABSENCE_CODES:CREATE";
        public const string Update = "PARAMS:ABSENCE_CODES:UPDATE";
    }

    public static class Affectations
    {
        public const string Read = "PARAMS:AFFECTATIONS";
        public const string Create = "PARAMS:AFFECTATIONS:CREATE";
        public const string Update = "PARAMS:AFFECTATIONS:UPDATE";
        public const string Delete = "PARAMS:AFFECTATIONS:DELETE";
    }

    public static class CardPaie
    {
        public const string Read = "PARAMS:CARDPAIE";
        public const string Create = "PARAMS:CARDPAIE:CREATE";
        public const string Update = "PARAMS:CARDPAIE:UPDATE";
        public const string Delete = "PARAMS:CARDPAIE:DELETE";
    }

    public static class Categories
    {
        public const string Read = "PARAMS:CATEGORIES";
        public const string Create = "PARAMS:CATEGORIES:CREATE";
        public const string Update = "PARAMS:CATEGORIES:UPDATE";
        public const string Delete = "PARAMS:CATEGORIES:DELETE";
    }

    public static class ClockParams
    {
        public const string Manage = "PARAMS:CLOCK_PARAMS";
    }

    public static class CodeConstantes
    {
        public const string Manage = "PARAMS:CODE_CONSTANTES";
    }

    public static class Correspondence
    {
        public const string Manage = "PARAMS:CORRESPONDENCE";
    }

    public static class Holidays
    {
        public const string Read = "PARAMS:HOLIDAYS";
        public const string Create = "PARAMS:HOLIDAYS:CREATE";
        public const string Update = "PARAMS:HOLIDAYS:UPDATE";
        public const string Delete = "PARAMS:HOLIDAYS:DELETE";
    }

    public static class Majorations
    {
        public const string Read = "PARAMS:MAJORATIONS";
        public const string Update = "PARAMS:MAJORATIONS:UPDATE";
    }

    public static class Privileges
    {
        public const string Read = "PARAMS:PRIVILEGES";
    }

    public static class Tolerances
    {
        public const string Read = "PARAMS:TOLERANCES";
        public const string Create = "PARAMS:TOLERANCES:CREATE";
        public const string Update = "PARAMS:TOLERANCES:UPDATE";
    }

    public static class Shifts
    {
        public const string Read = "PROC:SHIFTS";
        public const string Create = "PROC:SHIFTS:CREATE";
        public const string Update = "PROC:SHIFTS:UPDATE";
        public const string Delete = "PROC:SHIFTS:DELETE";
    }

    public static class EmployeePlanning
    {
        public const string Read = "PROC:EMPLOYEE_PLANNING";
        public const string Create = "PROC:EMPLOYEE_PLANNING:CREATE";
        public const string Update = "PROC:EMPLOYEE_PLANNING:UPDATE";
        public const string Delete = "PROC:EMPLOYEE_PLANNING:DELETE";
    }

    public static class Punches
    {
        public const string Manage = "PROC:PUNCHES";
    }

    public static class Anomalies
    {
        public const string Manage = "PROC:ANOMALIES";
        public const string Delete = "PROC:ANOMALIES:DELETE";
    }

    public static class CorrectedHours
    {
        public const string Read = "PROC:CORRECTED_HOURS";
        public const string Create = "PROC:CORRECTED_HOURS:CREATE";
        public const string Update = "PROC:CORRECTED_HOURS:UPDATE";
        public const string Delete = "PROC:CORRECTED_HOURS:DELETE";
    }

    public static class WeeklyValidation
    {
        public const string Manage = "PROC:WEEKLY_VALIDATION";
    }

    public static class Overtime
    {
        public const string Manage = "PROC:OVERTIME";
    }

    public static class Canteen
    {
        public const string Manage = "OTHER:CANTEEN";
    }

    public static class TravelExpenses
    {
        public const string Manage = "OTHER:TRAVEL_EXPENSES";
    }

    public static class Audit
    {
        public const string Read = "TRACE:AUDIT";
    }

    public static class Reports
    {
        public const string Read = "REPORTS:REPORTS";
    }
}
