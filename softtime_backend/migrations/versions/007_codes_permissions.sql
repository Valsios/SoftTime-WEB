/* =====================================================================================
   SoftTime — Migration 007 : codes de permission au format ressource.action — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : renomme les codes de dbo.T_ROLE_PERMISSION et dbo.T_USER_PERMISSION :
             - codes d'écran : « PARAMS:CATEGORIES:DELETE » -> « categories.delete »,
               « PROC:PUNCHES » -> « punches.manage », « SELF:READ » -> « self.read »… ;
             - codes de base : « SAGE:CREATE » -> « sage.create », « SAGE:<nom>:READ » -> « sage.<identifiant>.read »,
               de même pour POINTEUSE ; les droits sur une base qui n'existe plus sont supprimés (ils ne donnaient rien).
   Règles  : - seuls les codes à l'ancien format sont touchés : un second passage ne change rien ;
             - les marqueurs internes (__SOFTTIME_EMPTY_…) et le journal T_AUDIT_DROIT ne sont pas modifiés ;
             - seules les lignes de droits sur des bases disparues sont supprimées ; aucune table ni colonne n'est modifiée.
   ===================================================================================== */

SET NOCOUNT ON;
GO

DECLARE @codes TABLE (ANCIEN nvarchar(128) NOT NULL PRIMARY KEY, NOUVEAU nvarchar(128) NOT NULL);
INSERT INTO @codes (ANCIEN, NOUVEAU) VALUES
    (N'PARAMS:USERS:CREATE', N'users.create'),
    (N'PARAMS:USERS:UPDATE', N'users.update'),
    (N'PARAMS:USERS:DELETE', N'users.delete'),
    (N'PARAMS:USERS', N'users.read'),
    (N'PARAMS:ROLES:CREATE', N'roles.create'),
    (N'PARAMS:ROLES:UPDATE', N'roles.update'),
    (N'PARAMS:ROLES:DELETE', N'roles.delete'),
    (N'PARAMS:ROLES', N'roles.read'),
    (N'PARAMS:PRIVILEGES', N'privileges.read'),
    (N'PARAMS:DB_ACCESS', N'db-access.read'),
    (N'PARAMS:CLOCK_PARAMS', N'clock-params.manage'),
    (N'PARAMS:CORRESPONDENCE', N'correspondence.manage'),
    (N'PARAMS:CARDPAIE:CREATE', N'card-paie.create'),
    (N'PARAMS:CARDPAIE:UPDATE', N'card-paie.update'),
    (N'PARAMS:CARDPAIE:DELETE', N'card-paie.delete'),
    (N'PARAMS:CARDPAIE', N'card-paie.read'),
    (N'PARAMS:CATEGORIES:CREATE', N'categories.create'),
    (N'PARAMS:CATEGORIES:UPDATE', N'categories.update'),
    (N'PARAMS:CATEGORIES:DELETE', N'categories.delete'),
    (N'PARAMS:CATEGORIES', N'categories.read'),
    (N'PARAMS:AFFECTATIONS:CREATE', N'affectations.create'),
    (N'PARAMS:AFFECTATIONS:UPDATE', N'affectations.update'),
    (N'PARAMS:AFFECTATIONS:DELETE', N'affectations.delete'),
    (N'PARAMS:AFFECTATIONS', N'affectations.read'),
    (N'PARAMS:TOLERANCES:CREATE', N'tolerances.create'),
    (N'PARAMS:TOLERANCES:UPDATE', N'tolerances.update'),
    (N'PARAMS:TOLERANCES', N'tolerances.read'),
    (N'PARAMS:HOLIDAYS:CREATE', N'holidays.create'),
    (N'PARAMS:HOLIDAYS:UPDATE', N'holidays.update'),
    (N'PARAMS:HOLIDAYS:DELETE', N'holidays.delete'),
    (N'PARAMS:HOLIDAYS', N'holidays.read'),
    (N'PARAMS:MAJORATIONS:UPDATE', N'majorations.update'),
    (N'PARAMS:MAJORATIONS', N'majorations.read'),
    (N'PARAMS:CODE_CONSTANTES', N'code-constantes.manage'),
    (N'PARAMS:ABSENCE_CODES:CREATE', N'absence-codes.create'),
    (N'PARAMS:ABSENCE_CODES:UPDATE', N'absence-codes.update'),
    (N'PARAMS:ABSENCE_CODES', N'absence-codes.read'),
    (N'PROC:SHIFTS:CREATE', N'shifts.create'),
    (N'PROC:SHIFTS:UPDATE', N'shifts.update'),
    (N'PROC:SHIFTS:DELETE', N'shifts.delete'),
    (N'PROC:SHIFTS', N'shifts.read'),
    (N'PROC:EMPLOYEE_PLANNING:CREATE', N'employee-planning.create'),
    (N'PROC:EMPLOYEE_PLANNING:UPDATE', N'employee-planning.update'),
    (N'PROC:EMPLOYEE_PLANNING:DELETE', N'employee-planning.delete'),
    (N'PROC:EMPLOYEE_PLANNING', N'employee-planning.read'),
    (N'PROC:PUNCHES', N'punches.manage'),
    (N'PROC:ANOMALIES:DELETE', N'anomalies.delete'),
    (N'PROC:ANOMALIES', N'anomalies.manage'),
    (N'PROC:CORRECTED_HOURS:CREATE', N'corrected-hours.create'),
    (N'PROC:CORRECTED_HOURS:UPDATE', N'corrected-hours.update'),
    (N'PROC:CORRECTED_HOURS:DELETE', N'corrected-hours.delete'),
    (N'PROC:CORRECTED_HOURS', N'corrected-hours.read'),
    (N'PROC:WEEKLY_VALIDATION', N'weekly-validation.manage'),
    (N'PROC:OVERTIME', N'overtime.manage'),
    (N'OTHER:CANTEEN', N'canteen.manage'),
    (N'OTHER:TRAVEL_EXPENSES', N'travel-expenses.manage'),
    (N'TRACE:AUDIT', N'audit.read'),
    (N'REPORTS:REPORTS', N'reports.read'),
    (N'REPORTS:PREDICTIONS', N'predictions.read'),
    (N'REPORTS:CLOSING', N'closing.read'),
    (N'SELF:READ', N'self.read');

UPDATE p SET PERMISSION_CODE = c.NOUVEAU
FROM dbo.T_ROLE_PERMISSION AS p
JOIN @codes AS c ON c.ANCIEN = p.PERMISSION_CODE;
PRINT N'007 : codes d''écran renommés dans T_ROLE_PERMISSION : ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N'.';

UPDATE p SET PERMISSION_CODE = c.NOUVEAU
FROM dbo.T_USER_PERMISSION AS p
JOIN @codes AS c ON c.ANCIEN = p.PERMISSION_CODE;
PRINT N'007 : codes d''écran renommés dans T_USER_PERMISSION : ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N'.';
GO

/* Codes de base : « SAGE:CREATE » -> « sage.create » ; « SAGE:<nom>:READ » -> « sage.<identifiant>.read » (l'identifiant
   de la base, pour qu'un renommage ne fasse perdre aucun droit). Les droits sur une base qui n'existe plus ne donnaient
   rien : ils sont supprimés (nombre affiché). */
DECLARE @bases TABLE (ANCIEN nvarchar(128) NOT NULL PRIMARY KEY, NOUVEAU nvarchar(128) NULL);

INSERT INTO @bases (ANCIEN)
SELECT PERMISSION_CODE FROM dbo.T_ROLE_PERMISSION WHERE PERMISSION_CODE LIKE N'SAGE:%' OR PERMISSION_CODE LIKE N'POINTEUSE:%'
UNION
SELECT PERMISSION_CODE FROM dbo.T_USER_PERMISSION WHERE PERMISSION_CODE LIKE N'SAGE:%' OR PERMISSION_CODE LIKE N'POINTEUSE:%';

UPDATE @bases SET NOUVEAU = LOWER(LEFT(ANCIEN, CHARINDEX(N':', ANCIEN) - 1)) + N'.create'
WHERE ANCIEN IN (N'SAGE:CREATE', N'POINTEUSE:CREATE');

UPDATE c SET NOUVEAU = LOWER(d.PREFIXE) + N'.' + CAST(b.ID AS nvarchar(20)) + N'.' + LOWER(d.ACTION)
FROM @bases AS c
CROSS APPLY (SELECT
    LEFT(c.ANCIEN, CASE WHEN CHARINDEX(N':', c.ANCIEN) > 0 THEN CHARINDEX(N':', c.ANCIEN) - 1 ELSE 0 END) AS PREFIXE,
    RIGHT(c.ANCIEN, CASE WHEN CHARINDEX(N':', REVERSE(c.ANCIEN)) > 0 THEN CHARINDEX(N':', REVERSE(c.ANCIEN)) - 1 ELSE 0 END) AS ACTION) AS d
CROSS APPLY (SELECT SUBSTRING(c.ANCIEN, LEN(d.PREFIXE) + 2,
    CASE WHEN LEN(c.ANCIEN) - LEN(d.PREFIXE) - LEN(d.ACTION) - 2 > 0 THEN LEN(c.ANCIEN) - LEN(d.PREFIXE) - LEN(d.ACTION) - 2 ELSE 0 END) AS NOM) AS n
CROSS APPLY (SELECT MIN(x.ID) AS ID FROM (
    SELECT ID, NOM_BD FROM dbo.T_BDD_SAGE WHERE d.PREFIXE = N'SAGE'
    UNION ALL
    SELECT ID, NOM_BD FROM dbo.T_BDD_POINTEUSE WHERE d.PREFIXE = N'POINTEUSE') AS x
    WHERE x.NOM_BD = n.NOM) AS b
WHERE c.NOUVEAU IS NULL AND c.ANCIEN LIKE N'%:%:%' AND d.ACTION IN (N'READ', N'UPDATE', N'DELETE') AND b.ID IS NOT NULL;

UPDATE p SET PERMISSION_CODE = c.NOUVEAU FROM dbo.T_ROLE_PERMISSION AS p JOIN @bases AS c ON c.ANCIEN = p.PERMISSION_CODE
WHERE c.NOUVEAU IS NOT NULL;
PRINT N'007 : codes de base renommés dans T_ROLE_PERMISSION : ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N'.';
UPDATE p SET PERMISSION_CODE = c.NOUVEAU FROM dbo.T_USER_PERMISSION AS p JOIN @bases AS c ON c.ANCIEN = p.PERMISSION_CODE
WHERE c.NOUVEAU IS NOT NULL;
PRINT N'007 : codes de base renommés dans T_USER_PERMISSION : ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N'.';

DELETE p FROM dbo.T_ROLE_PERMISSION AS p JOIN @bases AS c ON c.ANCIEN = p.PERMISSION_CODE WHERE c.NOUVEAU IS NULL;
PRINT N'007 : droits sur des bases disparues retirés de T_ROLE_PERMISSION : ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N'.';
DELETE p FROM dbo.T_USER_PERMISSION AS p JOIN @bases AS c ON c.ANCIEN = p.PERMISSION_CODE WHERE c.NOUVEAU IS NULL;
PRINT N'007 : droits sur des bases disparues retirés de T_USER_PERMISSION : ' + CAST(@@ROWCOUNT AS nvarchar(20)) + N'.';
GO
