namespace SoftTime.Application.Security;

/// <summary>
/// Codes des permissions fines, à utiliser avec <c>[HasPermission]</c> : jamais de code écrit en dur dans un contrôleur.
/// Chaque constante doit être connue de <see cref="PermissionCatalog"/> (vérifié par PermissionsConstantsTests).
/// </summary>
public static class Permissions
{
    public static class AbsenceCodes
    {
        public const string Read = "absence-codes.read";
        public const string Create = "absence-codes.create";
        public const string Update = "absence-codes.update";
    }

    public static class Affectations
    {
        public const string Read = "affectations.read";
        public const string Create = "affectations.create";
        public const string Update = "affectations.update";
        public const string Delete = "affectations.delete";
    }

    public static class CardPaie
    {
        public const string Read = "card-paie.read";
        public const string Create = "card-paie.create";
        public const string Update = "card-paie.update";
        public const string Delete = "card-paie.delete";
    }

    public static class Categories
    {
        public const string Read = "categories.read";
        public const string Create = "categories.create";
        public const string Update = "categories.update";
        public const string Delete = "categories.delete";
    }

    public static class ClockParams
    {
        public const string Manage = "clock-params.manage";
    }

    public static class CodeConstantes
    {
        public const string Manage = "code-constantes.manage";
    }

    public static class Correspondence
    {
        public const string Manage = "correspondence.manage";
    }

    public static class Holidays
    {
        public const string Read = "holidays.read";
        public const string Create = "holidays.create";
        public const string Update = "holidays.update";
        public const string Delete = "holidays.delete";
    }

    public static class Majorations
    {
        public const string Read = "majorations.read";
        public const string Update = "majorations.update";
    }

    public static class Privileges
    {
        public const string Read = "privileges.read";
    }

    public static class Tolerances
    {
        public const string Read = "tolerances.read";
        public const string Create = "tolerances.create";
        public const string Update = "tolerances.update";
    }

    public static class Shifts
    {
        public const string Read = "shifts.read";
        public const string Create = "shifts.create";
        public const string Update = "shifts.update";
        public const string Delete = "shifts.delete";
    }

    public static class EmployeePlanning
    {
        public const string Read = "employee-planning.read";
        public const string Create = "employee-planning.create";
        public const string Update = "employee-planning.update";
        public const string Delete = "employee-planning.delete";
    }

    public static class Punches
    {
        public const string Manage = "punches.manage";
    }

    public static class Anomalies
    {
        public const string Manage = "anomalies.manage";
        public const string Delete = "anomalies.delete";
    }

    public static class CorrectedHours
    {
        public const string Read = "corrected-hours.read";
        public const string Create = "corrected-hours.create";
        public const string Update = "corrected-hours.update";
        public const string Delete = "corrected-hours.delete";
    }

    public static class WeeklyValidation
    {
        public const string Manage = "weekly-validation.manage";
    }

    public static class Overtime
    {
        public const string Manage = "overtime.manage";
    }

    public static class Canteen
    {
        public const string Manage = "canteen.manage";
    }

    public static class TravelExpenses
    {
        public const string Manage = "travel-expenses.manage";
    }

    public static class Audit
    {
        public const string Read = "audit.read";
    }

    public static class Reports
    {
        public const string Read = "reports.read";
    }
}
