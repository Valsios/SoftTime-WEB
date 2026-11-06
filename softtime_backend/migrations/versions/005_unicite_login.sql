/* =====================================================================================
   SoftTime — Migration 005 : unicité des logins — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : - dbo.T_RESPONSABLE.LOGIN_CLE : colonne calculée persistée, clé de comparaison du login
               (sans espaces en bordure, en majuscules), la même que LoginKey côté application ;
               un login vide ou NULL reçoit une clé propre au compte (« #<id> » précédé d'une espace,
               impossible pour un vrai login, qui est toujours enregistré sans espace en tête) ;
             - UX_T_RESPONSABLE_LOGIN_CLE : index unique sur cette clé. Deux comptes ne peuvent plus
               partager un login, même en différant par la casse ou par des espaces.
   Doublons: si la base contient déjà des logins en double, l'index n'est PAS créé : la liste des
             doublons est affichée (PRINT) et la migration se termine sans erreur. Après avoir renommé
             les comptes en double, créer l'index à la main (instruction en fin de script).
   Règles  : - chaque objet n'est créé que s'il n'existe pas ; aucune colonne existante n'est modifiée ;
             - aucune instruction destructive, aucun secret.
   ===================================================================================== */

SET NOCOUNT ON;
-- Options requises pour indexer une colonne calculée.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'LOGIN_CLE') IS NULL
BEGIN
    EXEC (N'ALTER TABLE dbo.T_RESPONSABLE ADD LOGIN_CLE AS CAST(
        CASE WHEN NULLIF(LTRIM(RTRIM(LOGIN)), N'''') IS NULL
             THEN N'' #'' + CAST(IDRESPONSABLE AS nvarchar(20))
             ELSE UPPER(LTRIM(RTRIM(LOGIN))) END AS nvarchar(256)) PERSISTED;');
    PRINT N'005 : colonne calculée T_RESPONSABLE.LOGIN_CLE ajoutée.';
END
ELSE
    PRINT N'005 : colonne T_RESPONSABLE.LOGIN_CLE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_RESPONSABLE_LOGIN_CLE' AND object_id = OBJECT_ID(N'dbo.T_RESPONSABLE'))
BEGIN
    -- Concaténation par variable : la fonction d'agrégation de chaînes n'existe qu'à partir de SQL Server 2017.
    DECLARE @doublons nvarchar(max);
    SELECT @doublons = COALESCE(@doublons + N', ', N'') + d.LOGIN_CLE + N' (' + CAST(d.n AS nvarchar(10)) + N' comptes)'
    FROM (SELECT LOGIN_CLE, COUNT(*) AS n FROM dbo.T_RESPONSABLE GROUP BY LOGIN_CLE HAVING COUNT(*) > 1) AS d;

    IF @doublons IS NULL
    BEGIN
        EXEC (N'CREATE UNIQUE INDEX UX_T_RESPONSABLE_LOGIN_CLE ON dbo.T_RESPONSABLE (LOGIN_CLE);');
        PRINT N'005 : index unique UX_T_RESPONSABLE_LOGIN_CLE créé.';
    END
    ELSE
    BEGIN
        PRINT N'005 : ATTENTION, logins en double (comparés sans casse ni espaces) : ' + @doublons;
        PRINT N'005 : index unique NON créé. Renommez ces comptes dans l''écran Utilisateurs, puis exécutez :';
        PRINT N'      CREATE UNIQUE INDEX UX_T_RESPONSABLE_LOGIN_CLE ON dbo.T_RESPONSABLE (LOGIN_CLE);';
    END
END
GO
