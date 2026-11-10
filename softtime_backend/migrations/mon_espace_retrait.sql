/* =====================================================================================
   Mon espace salarié — RETRAIT (SCRIPT FOURNI, JAMAIS EXÉCUTÉ AUTOMATIQUEMENT)

   À lancer seulement si la fonction est abandonnée, APRÈS avoir désactivé SelfService__Enabled et retiré le code.
   Il liste d'abord ce qui sera supprimé (comptes « Salarié » compris) : relire avant de décommenter la partie DELETE.
   ===================================================================================== */

SET NOCOUNT ON;

DECLARE @idRole decimal(18,0) = (SELECT TOP (1) IDROLE FROM dbo.T_ROLE WHERE NOMROLE = N'Salarié' ORDER BY IDROLE);

-- 1. Ce qui existe (lecture seule)
SELECT 'Comptes Salarié' AS Element, COUNT(*) AS Nombre FROM dbo.T_RESPONSABLE WHERE IDROLE = @idRole
UNION ALL SELECT 'Accès bases de ces comptes', COUNT(*) FROM dbo.T_BDD_AUTORISE WHERE IDRESPONSABLE IN (SELECT IDRESPONSABLE FROM dbo.T_RESPONSABLE WHERE IDROLE = @idRole)
UNION ALL SELECT 'Ajustements de permissions de ces comptes', COUNT(*) FROM dbo.T_USER_PERMISSION WHERE IDRESPONSABLE IN (SELECT IDRESPONSABLE FROM dbo.T_RESPONSABLE WHERE IDROLE = @idRole)
UNION ALL SELECT 'Permissions du rôle', COUNT(*) FROM dbo.T_ROLE_PERMISSION WHERE IDROLE = @idRole;

/* 2. Suppression — décommenter après relecture (ordre : dépendances d'abord).
BEGIN TRANSACTION;
DELETE FROM dbo.T_BDD_AUTORISE   WHERE IDRESPONSABLE IN (SELECT IDRESPONSABLE FROM dbo.T_RESPONSABLE WHERE IDROLE = @idRole);
DELETE FROM dbo.T_USER_PERMISSION WHERE IDRESPONSABLE IN (SELECT IDRESPONSABLE FROM dbo.T_RESPONSABLE WHERE IDROLE = @idRole);
DELETE FROM dbo.T_RESPONSABLE    WHERE IDROLE = @idRole;
DELETE FROM dbo.T_ROLE_PERMISSION WHERE IDROLE = @idRole;
DELETE FROM dbo.T_ROLE           WHERE IDROLE = @idRole;
COMMIT;
*/
