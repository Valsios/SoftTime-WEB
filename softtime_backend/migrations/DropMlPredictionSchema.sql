/* =====================================================================
   Suppression de l'ancien schéma « ml » (moteur prédictif retiré).

   L'application ne lit plus et n'écrit plus ce schéma. Ce script supprime
   DÉFINITIVEMENT les tables analytiques, les modèles et les prévisions
   stockés, ainsi que la trace de la migration EF qui les avait créés.

   - Jamais lancé automatiquement : à exécuter à la main, après sauvegarde.
   - Idempotent : sans effet sur une base qui n'a pas (ou plus) le schéma.
   - À lancer sur la base SoftTime, ou sur la base dédiée si
     ConnectionStrings:SoftTimeMl avait été renseignée.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'[ml].[Prediction]', N'U') IS NOT NULL DROP TABLE [ml].[Prediction];
IF OBJECT_ID(N'[ml].[Modele]', N'U') IS NOT NULL DROP TABLE [ml].[Modele];
IF OBJECT_ID(N'[ml].[SalarieSemaine]', N'U') IS NOT NULL DROP TABLE [ml].[SalarieSemaine];
IF OBJECT_ID(N'[ml].[SalarieJour]', N'U') IS NOT NULL DROP TABLE [ml].[SalarieJour];

-- Le schéma n'est supprimé que s'il ne contient plus aucun objet.
IF SCHEMA_ID(N'ml') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'ml'))
    EXEC(N'DROP SCHEMA [ml]');

IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
    DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] LIKE N'%[_]AddMlPredictionSchema';

COMMIT TRANSACTION;

PRINT 'Schéma ml : supprimé ou déjà absent.';
