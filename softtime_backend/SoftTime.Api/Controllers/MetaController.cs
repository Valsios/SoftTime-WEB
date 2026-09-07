using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SoftTime.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/meta")]
[Tags("Meta")]
public class MetaController : ControllerBase
{
    /// <summary>
    /// Catalogue des méthodes HTTP pour le frontend Angular (HttpClient).
    /// En-têtes: Authorization Bearer, X-Sage-Database, X-Pointeuse-Database.
    /// </summary>
    [HttpGet("endpoints")]
    public IActionResult Endpoints() => Ok(new
    {
        headers = new
        {
            Authorization = "Bearer {jwt}",
            XSageDatabase = "X-Sage-Database",
            XPointeuseDatabase = "X-Pointeuse-Database"
        },
        droits = new
        {
            databases = 1,
            parameters = 2,
            processing = 3,
            other = 4,
            traceability = 5,
            reports = 6
        },
        endpoints = ApiCatalog.All
    });
}

public static class ApiCatalog
{
    public static readonly object[] All =
    {
        E("Auth", "POST", "/api/auth/login", "Login, retourne JWT"),
        E("Auth", "GET", "/api/auth/me", "Utilisateur courant + bases SAGE"),
        E("Users", "GET", "/api/users", "Liste utilisateurs"),
        E("Users", "GET", "/api/users/{id}", "Détail utilisateur"),
        E("Users", "POST", "/api/users", "Créer utilisateur"),
        E("Users", "PUT", "/api/users/{id}", "Modifier utilisateur"),
        E("Users", "DELETE", "/api/users/{id}", "Supprimer utilisateur"),
        E("Roles", "GET", "/api/roles", "Liste rôles"),
        E("Roles", "POST", "/api/roles", "Créer rôle"),
        E("Roles", "PUT", "/api/roles/{id}", "Modifier rôle"),
        E("Roles", "DELETE", "/api/roles/{id}", "Supprimer rôle"),
        E("Privileges", "GET", "/api/privileges/droits", "Liste des droits (1-6)"),
        E("Privileges", "GET", "/api/privileges?roleId=", "Privilèges d'un rôle"),
        E("Privileges", "PUT", "/api/privileges/{roleId}", "Remplacer les droits du rôle (body: number[])"),
        E("DbAccess", "GET", "/api/db-access?userId=", "Bases SAGE autorisées"),
        E("DbAccess", "PUT", "/api/db-access/{userId}", "Remplacer accès (body: number[])"),
        E("SageDatabases", "GET", "/api/sage-databases", "Liste connexions SAGE"),
        E("SageDatabases", "POST", "/api/sage-databases", "Créer connexion SAGE"),
        E("SageDatabases", "PUT", "/api/sage-databases/{id}", "Modifier connexion SAGE"),
        E("SageDatabases", "DELETE", "/api/sage-databases/{id}", "Supprimer connexion SAGE"),
        E("PointeuseDatabases", "GET", "/api/pointeuse-databases", "Liste connexions pointeuse"),
        E("PointeuseDatabases", "POST", "/api/pointeuse-databases", "Créer connexion pointeuse"),
        E("PointeuseDatabases", "PUT", "/api/pointeuse-databases/{id}", "Modifier connexion pointeuse"),
        E("PointeuseDatabases", "DELETE", "/api/pointeuse-databases/{id}", "Supprimer connexion pointeuse"),
        E("ClockParams", "GET", "/api/clock-params", "Mode multi-pointeuse"),
        E("ClockParams", "PUT", "/api/clock-params", "Enregistrer T_CLOCK"),
        E("CorrespondenceMode", "GET", "/api/correspondence-mode", "Correspondance manuelle on/off"),
        E("CorrespondenceMode", "PUT", "/api/correspondence-mode", "Activer/désactiver correspondance"),
        E("CardPaie", "GET", "/api/cardpaie", "Correspondances SAGE ↔ badge"),
        E("CardPaie", "POST", "/api/cardpaie", "Créer correspondance"),
        E("CardPaie", "PUT", "/api/cardpaie/{id}", "Modifier correspondance"),
        E("CardPaie", "DELETE", "/api/cardpaie/{id}", "Supprimer correspondance"),
        E("CardPaie", "POST", "/api/cardpaie/auto-map", "Auto-mapping matricule/SSN"),
        E("Categories", "GET", "/api/categories", "Liste catégories"),
        E("Categories", "POST", "/api/categories", "Créer catégorie"),
        E("Categories", "PUT", "/api/categories/{id}", "Modifier catégorie"),
        E("Categories", "DELETE", "/api/categories/{id}", "Supprimer catégorie"),
        E("Affectations", "GET", "/api/affectations?categoryId=", "Salariés par catégorie"),
        E("Affectations", "POST", "/api/affectations", "Créer affectation"),
        E("Affectations", "PUT", "/api/affectations/{id}", "Modifier affectation"),
        E("Affectations", "DELETE", "/api/affectations/{id}", "Supprimer affectation"),
        E("Tolerances", "GET", "/api/tolerances?categoryId=", "Tolérances entrée/sortie"),
        E("Tolerances", "POST", "/api/tolerances", "Créer tolérance"),
        E("Tolerances", "PUT", "/api/tolerances/{id}", "Modifier tolérance"),
        E("Holidays", "GET", "/api/holidays", "Jours fériés"),
        E("Holidays", "POST", "/api/holidays", "Créer férié"),
        E("Holidays", "PUT", "/api/holidays/{id}", "Modifier férié"),
        E("Holidays", "DELETE", "/api/holidays/{id}", "Supprimer férié"),
        E("Holidays", "POST", "/api/holidays/import-sage", "Importer fériés calendrier SAGE"),
        E("Majorations", "GET", "/api/majorations", "Taux NUIT/DIMANCHE/FERIE"),
        E("Majorations", "PUT", "/api/majorations/{id}", "Modifier cotation"),
        E("AbsenceCodes", "GET", "/api/absence-codes", "Codes absence"),
        E("AbsenceCodes", "POST", "/api/absence-codes", "Créer code"),
        E("AbsenceCodes", "PUT", "/api/absence-codes/{id}", "Modifier code"),
        E("AbsenceCodes", "POST", "/api/absence-codes/sync-sage", "Synchroniser T_GHR"),
        E("Shifts", "POST", "/api/shifts/import-excel", "Import Excel shifts"),
        E("CardPaie", "POST", "/api/cardpaie/import-csv", "Import CSV correspondances badge;matricule"),
        E("Affectations", "POST", "/api/affectations/import-excel", "Import Excel affectations"),
        E("Excel", "POST", "/api/excel/export", "Export grille vers .xlsx"),
        E("Shifts", "GET", "/api/shifts", "Catalogue shifts"),
        E("Shifts", "POST", "/api/shifts", "Créer shift"),
        E("Shifts", "PUT", "/api/shifts/{id}", "Modifier shift"),
        E("Shifts", "DELETE", "/api/shifts/{id}", "Supprimer shift"),
        E("EmployeePlanning", "GET", "/api/employee-planning?from=&to=&cardId=", "Planning salarié"),
        E("EmployeePlanning", "POST", "/api/employee-planning", "Créer lignes planning (body: array)"),
        E("EmployeePlanning", "PUT", "/api/employee-planning", "Mettre à jour lignes planning"),
        E("EmployeePlanning", "DELETE", "/api/employee-planning/{id}", "Supprimer une ligne"),
        E("EmployeePlanning", "POST", "/api/employee-planning/import-excel", "Import Excel (multipart file)"),
        E("Punches", "GET", "/api/punches?from=&to=&matriculeFrom=&matriculeTo=", "Liste pointages"),
        E("Punches", "POST", "/api/punches/import-clock", "Import CHECKINOUT"),
        E("Punches", "POST", "/api/punches/import-excel", "Import Excel pointages"),
        E("Anomalies", "GET", "/api/anomalies?from=&to=", "Heures anomalie"),
        E("Anomalies", "DELETE", "/api/anomalies/{id}", "Supprimer anomalie"),
        E("CorrectedHours", "GET", "/api/corrected-hours?from=&to=", "Heures corrigées"),
        E("CorrectedHours", "POST", "/api/corrected-hours", "Créer correction"),
        E("CorrectedHours", "PUT", "/api/corrected-hours/{id}", "Modifier correction"),
        E("CorrectedHours", "DELETE", "/api/corrected-hours/{id}", "Supprimer correction"),
        E("CorrectedHours", "POST", "/api/corrected-hours/import-excel", "Import Excel corrections"),
        E("WeeklyValidation", "POST", "/api/weekly-validation/preview", "Calcul HT/HS semaine (lundi-dimanche)"),
        E("WeeklyValidation", "POST", "/api/weekly-validation/validate", "Valider la semaine"),
        E("WeeklyValidation", "POST", "/api/weekly-validation/unvalidate", "Annuler validation"),
        E("Overtime", "POST", "/api/overtime/calculate", "Calcul EXO/IMPO (plafond 20h)"),
        E("Overtime", "POST", "/api/overtime/purge", "Purger HS période"),
        E("Overtime", "POST", "/api/overtime/sync-sage", "Pousser HS vers T_CUMSAL SAGE"),
        E("Reports", "GET", "/api/reports/pointage?from=&to=", "État pointage"),
        E("Reports", "GET", "/api/reports/absences?from=&to=", "État absences"),
        E("Reports", "GET", "/api/reports/retards?from=&to=", "État retards"),
        E("Reports", "GET", "/api/reports/hs-recap?from=&to=", "Récap HS EXO/IMPO"),
        E("Reports", "GET", "/api/reports/heures-semaine?from=&to=", "Heures par semaine"),
        E("Reports", "GET", "/api/reports/heures-dimanche?from=&to=", "Heures dimanche"),
        E("Reports", "GET", "/api/reports/leave?matricule=&date=", "Congé SAGE du jour"),
        E("Canteen", "GET", "/api/canteen?from=&to=&matriculeFrom=&matriculeTo=", "Liste cantine"),
        E("Canteen", "POST", "/api/canteen/compute", "Calcul cantine"),
        E("TravelExpenses", "GET", "/api/travel-expenses?from=&to=&matriculeFrom=&matriculeTo=", "Liste frais déplacement"),
        E("TravelExpenses", "POST", "/api/travel-expenses/compute", "Calcul frais déplacement"),
        E("Audit", "GET", "/api/audit/config", "Colonnes auditées"),
        E("Audit", "POST", "/api/audit/config", "Créer config audit"),
        E("Audit", "PUT", "/api/audit/config/{id}", "Modifier config audit"),
        E("Audit", "GET", "/api/audit/events?from=&to=&table=&action=", "Journal suivi"),
        E("Audit", "GET", "/api/audit/schema", "Schéma découvert"),
        E("Health", "GET", "/health", "Santé API"),
        E("Meta", "GET", "/api/meta/endpoints", "Ce catalogue (pour Angular)")
    };

    private static object E(string group, string method, string path, string summary) => new { group, method, path, summary };
}
