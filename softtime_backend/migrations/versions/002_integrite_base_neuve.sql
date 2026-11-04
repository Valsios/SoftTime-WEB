/* =====================================================================================
   SoftTime — Migration 002 : intégrité d'une base NEUVE (unicité) — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : ajoute les index UNIQUES présents dans les anciennes bases mais absents d'une base
             créée par 001. Aujourd'hui un seul, sur une table commune aux deux :
               - UX_T_ACTIVECORRESP_PAIRE sur dbo.T_ACTIVECORRESP
                 (SAGE_SERVEUR, SAGE_BDD, POINTEUSE_SERVEUR, POINTEUSE_BDD), filtré SAGE_BDD IS NOT NULL.
   Règles  : - chaque objet est créé seulement s'il n'existe pas (nom d'index testé) ;
             - aucune instruction destructive, aucune colonne modifiée, aucun index existant modifié ;
             - si les colonnes ne sont pas indexables (nvarchar(max) dans une base neuve) ou si des
               doublons existent, l'index est IGNORÉ et un PRINT l'indique : le script n'échoue pas.
             - les index de performance non uniques ne sont pas créés ici (scripts manuels).
   Bornage des colonnes : voir migrations/add_pagination_performance_indexes.sql (manuel).
   ===================================================================================== */

SET NOCOUNT ON;

-- Options REQUISES pour un index unique FILTRÉ.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF OBJECT_ID(N'dbo.T_ACTIVECORRESP', N'U') IS NULL
    PRINT N'002 : dbo.T_ACTIVECORRESP absente, index UX_T_ACTIVECORRESP_PAIRE ignoré.';
ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_ACTIVECORRESP_PAIRE' AND object_id = OBJECT_ID(N'dbo.T_ACTIVECORRESP'))
    PRINT N'002 : UX_T_ACTIVECORRESP_PAIRE existe déjà, rien à faire.';
ELSE IF (SELECT COUNT(*) FROM sys.columns
         WHERE object_id = OBJECT_ID(N'dbo.T_ACTIVECORRESP')
           AND name IN (N'SAGE_SERVEUR', N'SAGE_BDD', N'POINTEUSE_SERVEUR', N'POINTEUSE_BDD')) < 4
    PRINT N'002 : colonnes de T_ACTIVECORRESP absentes, index UX_T_ACTIVECORRESP_PAIRE ignoré.';
ELSE IF EXISTS (SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.T_ACTIVECORRESP')
                  AND name IN (N'SAGE_SERVEUR', N'SAGE_BDD', N'POINTEUSE_SERVEUR', N'POINTEUSE_BDD')
                  AND max_length = -1)
    PRINT N'002 : colonnes de T_ACTIVECORRESP en nvarchar(max), non indexables : index UX_T_ACTIVECORRESP_PAIRE ignoré (voir add_pagination_performance_indexes.sql).';
ELSE IF (SELECT SUM(CAST(max_length AS int)) FROM sys.columns
         WHERE object_id = OBJECT_ID(N'dbo.T_ACTIVECORRESP')
           AND name IN (N'SAGE_SERVEUR', N'SAGE_BDD', N'POINTEUSE_SERVEUR', N'POINTEUSE_BDD')) > 1700
    PRINT N'002 : clé de T_ACTIVECORRESP trop large (> 1700 octets), index UX_T_ACTIVECORRESP_PAIRE ignoré.';
ELSE IF EXISTS (SELECT 1 FROM (
                    SELECT SAGE_SERVEUR, SAGE_BDD, POINTEUSE_SERVEUR, POINTEUSE_BDD
                    FROM dbo.T_ACTIVECORRESP
                    WHERE SAGE_BDD IS NOT NULL
                    GROUP BY SAGE_SERVEUR, SAGE_BDD, POINTEUSE_SERVEUR, POINTEUSE_BDD
                    HAVING COUNT(*) > 1) d)
    PRINT N'002 : doublons dans T_ACTIVECORRESP, index UX_T_ACTIVECORRESP_PAIRE ignoré (corrigez les doublons puis ajoutez un script).';
ELSE
BEGIN
    EXEC (N'CREATE UNIQUE INDEX UX_T_ACTIVECORRESP_PAIRE ON dbo.T_ACTIVECORRESP
            (SAGE_SERVEUR, SAGE_BDD, POINTEUSE_SERVEUR, POINTEUSE_BDD) WHERE SAGE_BDD IS NOT NULL');
    PRINT N'002 : index UX_T_ACTIVECORRESP_PAIRE créé.';
END
GO
