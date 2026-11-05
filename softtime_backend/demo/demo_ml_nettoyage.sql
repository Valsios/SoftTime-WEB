/*
  Retire les données de démonstration créées par demo_ml_donnees.sql :
  heures corrigées ORIGINE = 'DEMO_ML', planning de la plage d'ID enregistrée, et lignes de la
  base analytique (ml.*) calculées sur la période de démo pour base_sage.
  Ensuite, dans l'application : « Reconstruire l'historique » pour revenir à l'état réel.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Sage varchar(50) = 'base_sage';
DECLARE @Debut date = '2026-01-05';

BEGIN TRANSACTION;

DELETE FROM dbo.T_HEURECORRIGER WHERE ORIGINE = 'DEMO_ML';

IF OBJECT_ID('dbo.T_DEMO_ML_SEED') IS NOT NULL
BEGIN
    DELETE p FROM dbo.T_PLANNING_SAL p
    JOIN dbo.T_DEMO_ML_SEED s ON s.TableName = 'T_PLANNING_SAL' AND p.ID BETWEEN s.IdMin AND s.IdMax;
    DROP TABLE dbo.T_DEMO_ML_SEED;
END;

IF OBJECT_ID('ml.Prediction') IS NOT NULL
    DELETE FROM ml.Prediction WHERE BddSage = @Sage AND Cible >= @Debut;
IF OBJECT_ID('ml.SalarieJour') IS NOT NULL
    DELETE FROM ml.SalarieJour WHERE BddSage = @Sage AND [Date] >= @Debut;
IF OBJECT_ID('ml.SalarieSemaine') IS NOT NULL
    DELETE FROM ml.SalarieSemaine WHERE BddSage = @Sage AND SemaineDebut >= DATEADD(day, -6, @Debut);

COMMIT TRANSACTION;

SELECT 'Données de démo retirées.' AS Resultat;
