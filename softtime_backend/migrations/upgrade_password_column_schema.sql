-- =====================================================================================
-- SCRIPT DE MIGRATION DU SCHÉMA SQL (DBA) - SoftTime-WEB
-- =====================================================================================
-- Fichier      : upgrade_password_column_schema.sql
-- Composant    : Base de données SoftTime / Table dbo.T_RESPONSABLE
-- Colonne      : PASSWORD
--
-- MOTIVATION ET SÉCURITÉ :
-- Ce script est STRICTEMENT réservé à une exécution par un administrateur de base de
-- données (DBA) dans le cadre d'une fenêtre de maintenance planifiée.
-- L'application web ne doit JAMAIS exécuter d'instructions DDL (ALTER TABLE) au démarrage.
--
-- GARANTIES DE SÉCURITÉ DU SCRIPT :
-- 1. Préservation de la nullabilité : Si la colonne est NOT NULL, elle RESTE NOT NULL.
--    Si elle est NULL, elle reste NULL. Aucune contrainte n'est dégradée silencieusement.
-- 2. Choix du type nvarchar(256) : Préféré à nvarchar(max) car nvarchar(max) est un type
--    LOB (Large Object) stocké hors-page, incompatible avec l'indexation et les contraintes.
--    Les hachages ASP.NET Core Identity V3 (84 ou 124 caractères) et V2 (68 caractères)
--    s'intègrent parfaitement dans nvarchar(256).
-- 3. Idempotence : Le script peut être rejoué sans provoquer d'erreur si la colonne est déjà
--    correctement dimensionnée.
-- 4. Transaction sécurisée : Exécuté dans un bloc TRANSACTION avec ROLLBACK automatique en
--    cas d'erreur inattendue.
-- =====================================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT N'-------------------------------------------------------------------------------------';
PRINT N'Démarrage de la vérification du schéma de dbo.T_RESPONSABLE.PASSWORD...';
PRINT N'-------------------------------------------------------------------------------------';

BEGIN TRANSACTION;

BEGIN TRY
    -- 1. Vérifier l'existence de la table
    IF OBJECT_ID(N'dbo.T_RESPONSABLE', N'U') IS NULL
    BEGIN
        RAISERROR(N'Erreur critique : La table dbo.T_RESPONSABLE n''existe pas dans la base de données courante.', 16, 1);
    END;

    -- 2. Inspecter les métadonnées actuelles de la colonne PASSWORD
    DECLARE @CurrentDataType nvarchar(128);
    DECLARE @CurrentLength int;
    DECLARE @IsNullable varchar(3);

    SELECT 
        @CurrentDataType = DATA_TYPE,
        @CurrentLength = CHARACTER_MAXIMUM_LENGTH,
        @IsNullable = IS_NULLABLE
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME = 'T_RESPONSABLE'
      AND COLUMN_NAME = 'PASSWORD';

    IF @CurrentDataType IS NULL
    BEGIN
        RAISERROR(N'Erreur critique : La colonne PASSWORD n''existe pas dans dbo.T_RESPONSABLE.', 16, 1);
    END;

    PRINT N'Schéma actuel détecté : ' + @CurrentDataType + N'(' + 
          CASE WHEN @CurrentLength = -1 THEN N'max' ELSE CAST(@CurrentLength AS nvarchar(10)) END + 
          N'), Nullabilité : ' + @IsNullable;

    -- 3. Évaluer si une extension de colonne est requise
    -- V2 = 68 caractères, V3 = 84 caractères (SHA-256) ou 124 caractères (SHA-512)
    -- Si la colonne est déjà nvarchar(max) (-1) ou nvarchar(>= 128), elle est conforme.
    IF @CurrentLength = -1 OR @CurrentLength >= 128
    BEGIN
        PRINT N'CONFORME : La colonne PASSWORD est déjà dimensionnée pour accueillir les hachages cryptographiques (longueur >= 128).';
        PRINT N'Aucune modification DDL n''est nécessaire.';
    END
    ELSE
    BEGIN
        PRINT N'ATTENTION : La colonne PASSWORD actuelle (' + CAST(@CurrentLength AS nvarchar(10)) + N' car.) est trop courte pour un hachage V3 (>= 84 car.).';
        PRINT N'Préparation de la mise à niveau vers nvarchar(256)...';

        DECLARE @SqlDdl nvarchar(max);

        -- Préservation stricte de la contrainte de nullabilité existante
        IF @IsNullable = 'NO'
        BEGIN
            PRINT N'Application de : ALTER TABLE dbo.T_RESPONSABLE ALTER COLUMN PASSWORD nvarchar(256) NOT NULL;';
            SET @SqlDdl = N'ALTER TABLE dbo.T_RESPONSABLE ALTER COLUMN PASSWORD nvarchar(256) NOT NULL;';
        END
        ELSE
        BEGIN
            PRINT N'Application de : ALTER TABLE dbo.T_RESPONSABLE ALTER COLUMN PASSWORD nvarchar(256) NULL;';
            SET @SqlDdl = N'ALTER TABLE dbo.T_RESPONSABLE ALTER COLUMN PASSWORD nvarchar(256) NULL;';
        END;

        EXEC sp_executesql @SqlDdl;
        PRINT N'Extension de la colonne réussie avec préservation de la nullabilité (' + @IsNullable + N').';
    END;

    COMMIT TRANSACTION;
    PRINT N'-------------------------------------------------------------------------------------';
    PRINT N'Validation réussie. Schéma prêt pour le hachage sécurisé.';
    PRINT N'-------------------------------------------------------------------------------------';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    DECLARE @Msg nvarchar(4000) = ERROR_MESSAGE();
    DECLARE @Sev int = ERROR_SEVERITY();
    DECLARE @St int = ERROR_STATE();
    PRINT N'ÉCHEC ET ROLLBACK : ' + @Msg;
    RAISERROR(@Msg, @Sev, @St);
END CATCH;
GO
