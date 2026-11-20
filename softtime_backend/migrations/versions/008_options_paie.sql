/* =====================================================================================
   SoftTime — Migration 008 : options de paie par base SAGE — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : - dbo.T_PAIE_OPTION : petite table générique d'options de paie par base SAGE
               (une ligne par base et par clé : BDD_SAGE, CLE, VALEUR, MODIFIE_LE, MODIFIE_PAR).
               Première clé : « RemiseAZeroSansValeur » (« 1 » ou « 0 ») : à l'envoi vers la paie, remettre à 0 les
               salariés du périmètre sans valeur. Une base SANS ligne garde la valeur par défaut « 1 » (comportement
               validé en C2b) : aucune donnée n'est insérée par ce script.
   Règles  : - chaque objet n'est créé que s'il n'existe pas ; aucune donnée n'est insérée ni modifiée ;
             - aucune instruction destructive, aucun secret ;
             - les lots suivants (heures par jour, codes d'absence) y ajouteront d'autres clés sans nouvelle migration.
   Production sans AutoMigrate : exécuter ce fichier une fois sur la base SoftTime (sqlcmd -d <base> -i 008_options_paie.sql),
   puis le consigner dans T_SCHEMA_VERSION si l'AutoMigrate est activé plus tard (il le fera lui-même : script rejouable).
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.T_PAIE_OPTION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_PAIE_OPTION (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_PAIE_OPTION PRIMARY KEY,
        BDD_SAGE nvarchar(128) NOT NULL,
        CLE nvarchar(64) NOT NULL,
        VALEUR nvarchar(256) NOT NULL,
        MODIFIE_LE datetime2 NULL,
        MODIFIE_PAR nvarchar(256) NULL
    );
    PRINT N'008 : table T_PAIE_OPTION créée.';
END
ELSE
    PRINT N'008 : table T_PAIE_OPTION déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_PAIE_OPTION_BDD_CLE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_OPTION'))
    CREATE UNIQUE INDEX UX_T_PAIE_OPTION_BDD_CLE ON dbo.T_PAIE_OPTION (BDD_SAGE, CLE);
GO
