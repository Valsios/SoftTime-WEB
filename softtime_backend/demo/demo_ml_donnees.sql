/*
  Données de DÉMONSTRATION du moteur prédictif (base locale uniquement, jamais en production).

  Génère pour la société base_sage / pointeuse base_pointage :
    - le planning du 05/01/2026 au 29/11/2026 (motif hebdomadaire réel de chaque salarié, relevé
      sur sept.–nov. 2025), uniquement là où aucun planning n'existe déjà ;
    - les heures corrigées (T_HEURECORRIGER) du 05/01/2026 au 31/10/2026, avec des comportements
      apprenables : salariés plus ou moins absents (lundi, vendredi, lendemain de férié, août,
      absences de plusieurs jours), retards, heures sup plus fréquentes en fin de mois et dans
      certains départements.

  Tout est déterministe (même résultat à chaque exécution) et traçable :
    - heures corrigées : ORIGINE = 'DEMO_ML' ;
    - planning : plage d'ID enregistrée dans dbo.T_DEMO_ML_SEED.
  Retrait complet : demo_ml_nettoyage.sql.

  Ensuite, dans l'application : page Prévisions → « Reconstruire l'historique ».
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Sage varchar(50) = 'base_sage';
DECLARE @Pte varchar(50) = 'base_pointage';
DECLARE @Debut date = '2026-01-05';        -- un lundi
DECLARE @FinHisto date = '2026-10-31';     -- veille de la démo : dernier jour pointé
DECLARE @FinPlanning date = '2026-11-29';  -- horizon des prévisions (4 semaines)
DECLARE @MotifDu date = '2025-09-01', @MotifAu date = '2025-11-30';

IF OBJECT_ID('dbo.T_DEMO_ML_SEED') IS NULL
    CREATE TABLE dbo.T_DEMO_ML_SEED (
        TableName sysname NOT NULL,
        IdMin numeric(18, 0) NULL,
        IdMax numeric(18, 0) NULL,
        CreeLe datetime NOT NULL DEFAULT GETDATE());

IF EXISTS (SELECT 1 FROM dbo.T_DEMO_ML_SEED)
   OR EXISTS (SELECT 1 FROM dbo.T_HEURECORRIGER WHERE ORIGINE = 'DEMO_ML')
BEGIN
    RAISERROR('Des données de démo existent déjà : exécutez demo_ml_nettoyage.sql avant de relancer.', 16, 1);
    RETURN;
END;

-- ---------------------------------------------------------------- salariés et profils
IF OBJECT_ID('tempdb..#Sal') IS NOT NULL DROP TABLE #Sal;
SELECT
    c.ID AS IDSAL,
    c.SAGE_MATRICULE AS Matricule,
    -- Propension individuelle à l'absence : 8 % très absents, 22 % moyens, 70 % rares.
    CASE WHEN h.H1 < 8 THEN 0.10 WHEN h.H1 < 30 THEN 0.045 ELSE 0.012 END
      * CASE c.DEPARTEMENT WHEN 'BR796' THEN 1.6 WHEN 'BR730' THEN 1.25 WHEN 'BR800' THEN 0.7 ELSE 1.0 END AS PAbs,
    CASE WHEN h.H2 < 15 THEN 0.14 ELSE 0.025 END AS PRetard,
    -- Heures sup : plus fréquentes dans certains départements, modulées par salarié.
    CASE c.DEPARTEMENT WHEN 'BR780' THEN 0.32 WHEN 'BR710' THEN 0.24 WHEN 'BR797' THEN 0.20 ELSE 0.10 END
      * (0.5 + h.H3 / 100.0) AS PHs
INTO #Sal
FROM dbo.T_CARDPAIE c
CROSS APPLY (SELECT
    ABS(CHECKSUM(HASHBYTES('MD5', CONCAT('abs', LTRIM(RTRIM(c.SAGE_MATRICULE)))))) % 100 AS H1,
    ABS(CHECKSUM(HASHBYTES('MD5', CONCAT('ret', LTRIM(RTRIM(c.SAGE_MATRICULE)))))) % 100 AS H2,
    ABS(CHECKSUM(HASHBYTES('MD5', CONCAT('hs', LTRIM(RTRIM(c.SAGE_MATRICULE)))))) % 100 AS H3) h
WHERE c.SAGE_BDD = @Sage
  AND c.ACTIF = 1
  AND NULLIF(LTRIM(RTRIM(c.SAGE_MATRICULE)), '') IS NOT NULL
    OPTION (MAX_GRANT_PERCENT = 1);

-- ---------------------------------------------------------------- motif hebdomadaire réel
-- Jour de semaine indépendant de DATEFIRST : 0 = lundi … 6 = dimanche.
IF OBJECT_ID('tempdb..#Motif') IS NOT NULL DROP TABLE #Motif;
WITH Releves AS (
    SELECT p.IDSAL,
           DATEDIFF(day, '19000101', p.DateP) % 7 AS Js,
           p.NoSHIFT,
           CAST(p.HA AS time(0)) AS HaT,
           CAST(p.HD AS time(0)) AS HdT,
           ISNULL(p.Pause, '00:00') AS Pause,
           ISNULL(p.OffP, 0) AS OffP
    FROM dbo.T_PLANNING_SAL p
    JOIN #Sal s ON s.IDSAL = p.IDSAL
    WHERE p.BDD_SAGE = @Sage AND p.DateP BETWEEN @MotifDu AND @MotifAu
      AND p.HA IS NOT NULL AND p.HD IS NOT NULL
), Classes AS (
    SELECT IDSAL, Js, NoSHIFT, HaT, HdT, Pause, OffP,
           ROW_NUMBER() OVER (PARTITION BY IDSAL, Js ORDER BY COUNT(*) DESC, NoSHIFT) AS Rang
    FROM Releves
    GROUP BY IDSAL, Js, NoSHIFT, HaT, HdT, Pause, OffP
)
SELECT IDSAL, Js, NoSHIFT, HaT, HdT, Pause, OffP INTO #Motif FROM Classes WHERE Rang = 1
    OPTION (MAX_GRANT_PERCENT = 1);

-- ---------------------------------------------------------------- calendrier
IF OBJECT_ID('tempdb..#Jours') IS NOT NULL DROP TABLE #Jours;
WITH N AS (
    SELECT TOP (DATEDIFF(day, @Debut, @FinPlanning) + 1)
           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS I
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
SELECT DATEADD(day, I, @Debut) AS D,
       DATEDIFF(day, '19000101', DATEADD(day, I, @Debut)) % 7 AS Js
INTO #Jours FROM N;

IF OBJECT_ID('tempdb..#Feries') IS NOT NULL DROP TABLE #Feries;
SELECT DISTINCT CAST([DATE] AS date) AS D INTO #Feries
FROM dbo.T_FERIE WHERE BDD_SAGE = @Sage AND [DATE] IS NOT NULL;

-- ---------------------------------------------------------------- planning
IF OBJECT_ID('tempdb..#Plan') IS NOT NULL DROP TABLE #Plan;
SELECT m.IDSAL, j.D, j.Js, m.NoSHIFT, m.HaT, m.HdT, m.Pause, m.OffP,
       CAST(CAST(j.D AS datetime) + CAST(m.HaT AS datetime) AS datetime) AS HA,
       CAST(CAST(CASE WHEN m.HdT <= m.HaT AND m.HdT <> m.HaT THEN DATEADD(day, 1, j.D) ELSE j.D END AS datetime)
            + CAST(m.HdT AS datetime) AS datetime) AS HD
INTO #Plan
FROM #Motif m
JOIN #Jours j ON j.Js = m.Js
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.T_PLANNING_SAL p
    WHERE p.IDSAL = m.IDSAL AND p.DateP = j.D AND p.BDD_SAGE = @Sage)
    OPTION (MAX_GRANT_PERCENT = 1);

-- Planning déjà présent sur la période (ex. novembre 2026) : réutilisé tel quel pour les pointages.
IF OBJECT_ID('tempdb..#PlanPointe') IS NOT NULL DROP TABLE #PlanPointe;
SELECT IDSAL, D, Js, HaT, HdT, OffP INTO #PlanPointe FROM #Plan WHERE D <= @FinHisto;
INSERT INTO #PlanPointe (IDSAL, D, Js, HaT, HdT, OffP)
SELECT x.IDSAL, x.D, DATEDIFF(day, '19000101', x.D) % 7, x.HaT, x.HdT, x.OffP
FROM (
    SELECT p.IDSAL, p.DateP AS D, CAST(p.HA AS time(0)) AS HaT, CAST(p.HD AS time(0)) AS HdT,
           ISNULL(p.OffP, 0) AS OffP,
           ROW_NUMBER() OVER (PARTITION BY p.IDSAL, p.DateP ORDER BY p.ID) AS Rang
    FROM dbo.T_PLANNING_SAL p
    JOIN #Sal s ON s.IDSAL = p.IDSAL
    WHERE p.BDD_SAGE = @Sage AND p.DateP BETWEEN @Debut AND @FinHisto
      AND p.HA IS NOT NULL AND p.HD IS NOT NULL
) x
WHERE x.Rang = 1
    OPTION (MAX_GRANT_PERCENT = 1);

-- Jours réellement travaillés : ni OFF, ni shift vide (HA = HD), ni férié.
DELETE pp FROM #PlanPointe pp
WHERE pp.OffP = 1 OR pp.HaT = pp.HdT OR pp.D IN (SELECT D FROM #Feries);

-- Index des tables de travail, et OPTION (MAX_GRANT_PERCENT = 1) sur les grosses requêtes :
-- sur un SQL Server Express à court de mémoire, une allocation trop grosse attend indéfiniment.
CREATE CLUSTERED INDEX IX_Sal ON #Sal (IDSAL);
CREATE CLUSTERED INDEX IX_PlanPointe ON #PlanPointe (IDSAL, D);

-- ---------------------------------------------------------------- tirages
-- Pseudo-aléatoire déterministe : CHECKSUM d'une clé (salarié, jour, tirage) ramené à [0, 1).
IF OBJECT_ID('tempdb..#Tirage') IS NOT NULL DROP TABLE #Tirage;
CREATE TABLE #Tirage (
    IDSAL int NOT NULL, Matricule varchar(50) NOT NULL, D date NOT NULL, Js int NOT NULL,
    HaT time(0) NOT NULL, HdT time(0) NOT NULL,
    PAbs float NOT NULL, PRetard float NOT NULL, PHs float NOT NULL, PDebut float NOT NULL,
    R1 float, R2 float, R3 float, R4 float, R5 float, R6 float, R7 float,
    PRIMARY KEY (IDSAL, D));

INSERT INTO #Tirage
SELECT pp.IDSAL, s.Matricule, pp.D, pp.Js, pp.HaT, pp.HdT, s.PAbs, s.PRetard, s.PHs,
       -- Probabilité de début d'absence du jour.
       s.PAbs
         * CASE pp.Js WHEN 0 THEN 1.7 WHEN 4 THEN 1.35 ELSE 1.0 END
         * CASE WHEN DATEADD(day, -1, pp.D) IN (SELECT D FROM #Feries) THEN 1.8 ELSE 1.0 END
         * CASE WHEN MONTH(pp.D) = 8 THEN 1.3 WHEN MONTH(pp.D) IN (1, 2) THEN 1.15 ELSE 1.0 END
         / 1.6,
       NULL, NULL, NULL, NULL, NULL, NULL, NULL
FROM #PlanPointe pp
JOIN #Sal s ON s.IDSAL = pp.IDSAL
    OPTION (MAX_GRANT_PERCENT = 1);

-- Un seul MD5 par ligne, découpé en 7 tranches de 2 octets (CHECKSUM seul varie presque
-- linéairement avec la date : les tirages de jours consécutifs seraient quasi identiques).
UPDATE t SET
    R1 = CAST(SUBSTRING(h.H, 1, 2) AS int) / 65536.0,
    R2 = CAST(SUBSTRING(h.H, 3, 2) AS int) / 65536.0,
    R3 = CAST(SUBSTRING(h.H, 5, 2) AS int) / 65536.0,
    R4 = CAST(SUBSTRING(h.H, 7, 2) AS int) / 65536.0,
    R5 = CAST(SUBSTRING(h.H, 9, 2) AS int) / 65536.0,
    R6 = CAST(SUBSTRING(h.H, 11, 2) AS int) / 65536.0,
    R7 = CAST(SUBSTRING(h.H, 13, 2) AS int) / 65536.0
FROM #Tirage t
CROSS APPLY (SELECT HASHBYTES('MD5', CONCAT('demo-ml|', t.IDSAL, '|', CONVERT(char(8), t.D, 112))) AS H) h
OPTION (MAX_GRANT_PERCENT = 1);

-- Arrêts : un début dure 1 à 3 jours calendaires (plus long pour les salariés très absents).
IF OBJECT_ID('tempdb..#Debuts') IS NOT NULL DROP TABLE #Debuts;
SELECT IDSAL, D,
       1 + CASE WHEN PAbs >= 0.08 THEN CAST(R2 * 3 AS int) ELSE CAST(R2 * 2 AS int) END AS Longueur
INTO #Debuts FROM #Tirage WHERE R1 < PDebut
    OPTION (MAX_GRANT_PERCENT = 1);
CREATE CLUSTERED INDEX IX_Debuts ON #Debuts (IDSAL, D);

IF OBJECT_ID('tempdb..#Absents') IS NOT NULL DROP TABLE #Absents;
CREATE TABLE #Absents (IDSAL int NOT NULL, D date NOT NULL, PRIMARY KEY (IDSAL, D));
INSERT INTO #Absents (IDSAL, D)
SELECT DISTINCT t.IDSAL, t.D
FROM #Tirage t
JOIN #Debuts d ON d.IDSAL = t.IDSAL
              AND d.D BETWEEN DATEADD(day, -3, t.D) AND t.D
              AND DATEDIFF(day, d.D, t.D) < d.Longueur
    OPTION (MAX_GRANT_PERCENT = 1);

-- ---------------------------------------------------------------- insertion
BEGIN TRANSACTION;

DECLARE @PlanAvant int = ISNULL((SELECT MAX(ID) FROM dbo.T_PLANNING_SAL), 0);
INSERT INTO dbo.T_PLANNING_SAL (IDSAL, NoSHIFT, DateP, HA, Pause, HD, OffP, BDD_SAGE, BDD_POINTEUSE)
SELECT IDSAL, NoSHIFT, D, HA, Pause, HD, OffP, @Sage, @Pte
FROM #Plan ORDER BY IDSAL, D
    OPTION (MAX_GRANT_PERCENT = 1);
INSERT INTO dbo.T_DEMO_ML_SEED (TableName, IdMin, IdMax)
SELECT 'T_PLANNING_SAL', MIN(ID), MAX(ID) FROM dbo.T_PLANNING_SAL WHERE ID > @PlanAvant;

INSERT INTO dbo.T_HEURECORRIGER
    (BDD_SAGE, BDD_POINTEUSE, MATRICULE_SAGE, DATE_POINTAGE_IN, DATE_POINTAGE_OUT, HENTREE, HSORTIE, ORIGINE)
SELECT @Sage, @Pte, t.Matricule,
       CAST(t.D AS datetime),
       CAST(CASE WHEN t.HdT < t.HaT THEN DATEADD(day, 1, t.D) ELSE t.D END AS datetime),
       -- Entrée : un peu en avance en général ; retard de 8 à 43 min (plus fréquent le lundi).
       CAST(DATEADD(second,
            CASE WHEN t.R3 < t.PRetard * CASE WHEN t.Js = 0 THEN 1.4 ELSE 1.0 END
                 THEN 480 + CAST(t.R4 * 2100 AS int)
                 ELSE -CAST(t.R6 * 600 AS int) END,
            t.HaT) AS time(0)),
       -- Sortie : heures sup de 30 min à 2 h 30, plus fréquentes la dernière semaine du mois.
       CAST(DATEADD(second,
            CASE WHEN t.R5 < t.PHs * CASE WHEN DAY(t.D) > DAY(EOMONTH(t.D)) - 7 THEN 1.7 ELSE 1.0 END
                 THEN 1800 + CAST(t.R7 * 7200 AS int)
                 ELSE CAST(t.R6 * 420 AS int) END,
            t.HdT) AS time(0)),
       'DEMO_ML'
FROM #Tirage t
WHERE NOT EXISTS (SELECT 1 FROM #Absents a WHERE a.IDSAL = t.IDSAL AND a.D = t.D)
    OPTION (MAX_GRANT_PERCENT = 1);

INSERT INTO dbo.T_DEMO_ML_SEED (TableName, IdMin, IdMax)
SELECT 'T_HEURECORRIGER', MIN(ID), MAX(ID) FROM dbo.T_HEURECORRIGER WHERE ORIGINE = 'DEMO_ML';

COMMIT TRANSACTION;

-- ---------------------------------------------------------------- résumé
SELECT
    (SELECT COUNT(*) FROM #Sal) AS Salaries,
    (SELECT COUNT(DISTINCT IDSAL) FROM #Motif) AS AvecMotif,
    (SELECT COUNT(*) FROM #Plan) AS PlanningInsere,
    (SELECT COUNT(*) FROM #Tirage) AS JoursTravaillables,
    (SELECT COUNT(*) FROM #Absents) AS JoursAbsents,
    (SELECT COUNT(*) FROM dbo.T_HEURECORRIGER WHERE ORIGINE = 'DEMO_ML') AS PointagesInseres;
