/* =====================================================================================
   Mon espace salarié — rôle « Salarié » (SCRIPT FOURNI, JAMAIS EXÉCUTÉ AUTOMATIQUEMENT)

   Crée le rôle « Salarié » avec UNE seule permission, SELF:READ, et AUCUNE porte (aucune ligne T_DROIT_ROLE).
   Aucune permission SAGE: ni POINTEUSE: : elles donneraient la porte 1 (PermissionCatalog.DeduirePortes).

   Pourquoi ce fichier n'est PAS dans migrations/versions : les scripts de ce dossier sont embarqués et appliqués
   automatiquement en développement (Database:AutoMigrate = true). Ce rôle ne doit être créé que sur décision,
   après validation de la fonction par le manager. À exécuter à la main, une seule fois ; idempotent.

   Ensuite (écran Utilisateurs) : créer chaque compte avec ce rôle, renseigner le matricule SAGE et l'e-mail,
   cocher UNE SEULE base dans « Accès bases » (aucune autre permission ne doit être ajoutée au compte).
   Activation : SelfService__Enabled=true (variable d'environnement), jamais dans appsettings.json.
   Retrait : mon_espace_retrait.sql.
   ===================================================================================== */

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.T_ROLE_PERMISSION', N'U') IS NULL
BEGIN
    RAISERROR(N'T_ROLE_PERMISSION est absente : démarrez l''API une fois (elle la crée) avant ce script.', 16, 1);
    RETURN;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.T_ROLE WHERE NOMROLE = N'Salarié')
    INSERT INTO dbo.T_ROLE (NOMROLE) VALUES (N'Salarié');
GO

DECLARE @idRole decimal(18,0) = (SELECT TOP (1) IDROLE FROM dbo.T_ROLE WHERE NOMROLE = N'Salarié' ORDER BY IDROLE);

IF NOT EXISTS (SELECT 1 FROM dbo.T_ROLE_PERMISSION WHERE IDROLE = @idRole AND PERMISSION_CODE = N'SELF:READ')
    INSERT INTO dbo.T_ROLE_PERMISSION (IDROLE, PERMISSION_CODE, IS_GRANTED) VALUES (@idRole, N'SELF:READ', 1);

SELECT IDROLE, NOMROLE FROM dbo.T_ROLE WHERE IDROLE = @idRole;
GO
