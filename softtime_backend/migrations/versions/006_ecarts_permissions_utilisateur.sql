/* =====================================================================================
   SoftTime — Migration 006 : droits des comptes stockés en écarts — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : dbo.T_USER_PERMISSION.IS_DEVIATION (bit, NOT NULL, défaut 0).
             - 0 : ancienne copie complète de la liste des droits du compte (lignes existantes) ;
             - 1 : écart par rapport au rôle (IS_GRANTED = 1 ajout, 0 retrait), format de toute
               nouvelle ligne.
             La reprise des données (copies -> écarts, sans changer les droits effectifs) est faite
             par l'application au démarrage (UserPermissionDeviationMigration), qui réutilise le
             calcul des droits : ce script n'ajoute que la colonne.
   Règles  : - la colonne n'est créée que si elle n'existe pas ; aucune donnée n'est modifiée ;
             - aucune instruction destructive, aucun secret.
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF COL_LENGTH(N'dbo.T_USER_PERMISSION', N'IS_DEVIATION') IS NULL
BEGIN
    ALTER TABLE dbo.T_USER_PERMISSION
        ADD IS_DEVIATION bit NOT NULL CONSTRAINT DF_T_USER_PERMISSION_IS_DEVIATION DEFAULT (0);
    PRINT N'006 : colonne T_USER_PERMISSION.IS_DEVIATION ajoutée (lignes existantes = copies à convertir).';
END
ELSE
    PRINT N'006 : colonne T_USER_PERMISSION.IS_DEVIATION déjà présente.';
GO
