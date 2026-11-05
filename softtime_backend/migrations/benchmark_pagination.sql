/* ============================================================================
   Benchmark pagination — à exécuter AVANT puis APRÈS
   migrations/add_pagination_performance_indexes.sql.

   Objectif : comparer les lectures logiques et le temps CPU des requêtes
   réellement utilisées par les endpoints paginés. Le script ne modifie rien.

   Mode d'emploi :
     1. Choisir une base et une période représentatives (volume proche de la
        production, par exemple un mois avec plusieurs milliers de pointages).
     2. Remplacer @sageDb / @from / @to ci-dessous.
     3. Exécuter, noter les "logical reads" et le temps CPU de chaque bloc.
     4. Exécuter add_pagination_performance_indexes.sql.
     5. Rejouer ce script et comparer.

   Complément API : mesurer séparément les endpoints (20 appels à chaud) :
     Measure-Command { Invoke-RestMethod ... }
   et relever le nombre d'items (<= 50) et la taille de la réponse.

   Attendu après indexation : index seek/range sur (base SAGE, date, [matricule]),
   pas de Table Scan sur les filtres principaux, lectures logiques fortement
   réduites pour les pages peu profondes.
   ============================================================================ */

SET NOCOUNT ON;

DECLARE @sageDb   nvarchar(128) = N'BASE_SAGE';   -- <-- à adapter
DECLARE @from     datetime2     = '2026-01-01';   -- <-- à adapter
DECLARE @to       datetime2     = '2026-12-31';   -- <-- à adapter
DECLARE @pageSize int           = 50;
DECLARE @offset   int           = 0;               -- (page - 1) * @pageSize

SET STATISTICS IO ON;
SET STATISTICS TIME ON;

PRINT '=== 1. Pointages : COUNT filtré ===';
SELECT COUNT(*)
FROM dbo.T_POINTAGE
WHERE NOM_BDD_SAGE = @sageDb
  AND DATE_POINTAGE >= @from
  AND DATE_POINTAGE <= @to;

PRINT '=== 2. Pointages : page 1 (ORDER BY + OFFSET/FETCH) ===';
SELECT IDPOINTAGE, NOM_BDD_SAGE, MATRICULE_SAGE, DATE_POINTAGE, HEURE_POINTAGE,
       TYPE_POINTAGE, DATE_IMPORTATION
FROM dbo.T_POINTAGE
WHERE NOM_BDD_SAGE = @sageDb
  AND DATE_POINTAGE >= @from
  AND DATE_POINTAGE <= @to
ORDER BY DATE_POINTAGE, HEURE_POINTAGE, MATRICULE_SAGE, IDPOINTAGE
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

PRINT '=== 3. Heures corrigées : COUNT + page 1 ===';
SELECT COUNT(*)
FROM dbo.T_HEURECORRIGER
WHERE BDD_SAGE = @sageDb
  AND DATE_POINTAGE_IN >= @from
  AND DATE_POINTAGE_IN <= @to;

SELECT ID, BDD_SAGE, MATRICULE_SAGE, DATE_POINTAGE_IN, DATE_POINTAGE_OUT,
       HENTREE, HPS, HPE, HSORTIE, RETARD, HS
FROM dbo.T_HEURECORRIGER
WHERE BDD_SAGE = @sageDb
  AND DATE_POINTAGE_IN >= @from
  AND DATE_POINTAGE_IN <= @to
ORDER BY DATE_POINTAGE_IN, MATRICULE_SAGE, ID
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

PRINT '=== 4. Anomalies : COUNT + page 1 ===';
SELECT COUNT(*)
FROM dbo.T_HEUREANOMALIE
WHERE BDD_SAGE = @sageDb
  AND (DATE_POINTAGE_IN IS NULL
       OR (DATE_POINTAGE_IN >= @from AND DATE_POINTAGE_IN <= @to));

SELECT ID, MATRICULE_SAGE, DATE_POINTAGE_IN, DATE_POINTAGE_OUT, HENTREE, HPS, HPE, HSORTIE
FROM dbo.T_HEUREANOMALIE
WHERE BDD_SAGE = @sageDb
  AND (DATE_POINTAGE_IN IS NULL
       OR (DATE_POINTAGE_IN >= @from AND DATE_POINTAGE_IN <= @to))
ORDER BY DATE_POINTAGE_IN, MATRICULE_SAGE, ID
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

PRINT '=== 5. Historique pointages : COUNT + page 1 (tri suppression desc) ===';
SELECT COUNT(*)
FROM dbo.T_HISTORIQUE_POINTAGE
WHERE NOM_BDD_SAGE = @sageDb
  AND DATE_SUPPRESSION >= @from
  AND DATE_SUPPRESSION <= @to;

SELECT ID, MATRICULE_SAGE, DATE_POINTAGE, HEURE_POINTAGE, TYPE_POINTAGE, DATE_SUPPRESSION
FROM dbo.T_HISTORIQUE_POINTAGE
WHERE NOM_BDD_SAGE = @sageDb
  AND DATE_SUPPRESSION >= @from
  AND DATE_SUPPRESSION <= @to
ORDER BY DATE_SUPPRESSION DESC, ID DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

PRINT '=== 6. Cantine : COUNT + page 1 ===';
SELECT COUNT(*)
FROM dbo.T_CANTINE
WHERE NOM_BDD_SAGE = @sageDb
  AND DATEDEB >= @from
  AND DATEFIN <= @to;

SELECT IDCANTINE, MATRICULE_SAGE, DATEDEB, DATEFIN, TOTAL_CANTINE
FROM dbo.T_CANTINE
WHERE NOM_BDD_SAGE = @sageDb
  AND DATEDEB >= @from
  AND DATEFIN <= @to
ORDER BY MATRICULE_SAGE, DATEDEB, IDCANTINE
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

PRINT '=== 7. Audit événements : COUNT + page 1 (tri date desc) ===';
SELECT COUNT(*)
FROM dbo.suivi_SUIVI
WHERE [_DATETIMES] >= @from
  AND [_DATETIMES] <= @to;

SELECT Id, [_TABLES], [_COLONNES], [_USERS], [_ACTIONS], [_DATETIMES], [_NOUVEAUX], [_ANCIENS], [_BASES]
FROM dbo.suivi_SUIVI
WHERE [_DATETIMES] >= @from
  AND [_DATETIMES] <= @to
ORDER BY [_DATETIMES] DESC, Id DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

PRINT '=== 8. Cumuls HS EXO/IMPO : COUNT + page 1 ===';
SELECT COUNT(*)
FROM dbo.T_HSExoImp
WHERE BDD_SAGE = @sageDb
  AND PeriodeDebut <= @to
  AND PeriodeFin >= @from;

SELECT OID, Matricule, NumSal, EXO130, EXO150, I130, I150, FERIE, NUIT, DIM, RETARD, ABSENCE
FROM dbo.T_HSExoImp
WHERE BDD_SAGE = @sageDb
  AND PeriodeDebut <= @to
  AND PeriodeFin >= @from
ORDER BY Matricule, OID
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

PRINT '=== 9. Planning salarié : jours planifiés par carte (agrégat) ===';
SELECT IDSAL, COUNT(*) AS PlannedDays
FROM dbo.T_PLANNING_SAL
WHERE BDD_SAGE = @sageDb
  AND DateP >= @from
  AND DateP < DATEADD(day, 1, @to)
GROUP BY IDSAL;

PRINT '=== 10. Validation semaine : agrégat des heures corrigées par salarié ===';
SELECT MATRICULE_SAGE, COUNT(*) AS Lignes
FROM dbo.T_HEURECORRIGER
WHERE BDD_SAGE = @sageDb
  AND DATE_POINTAGE_IN >= @from
  AND DATE_POINTAGE_IN <= @to
GROUP BY MATRICULE_SAGE;

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;

PRINT 'Benchmark terminé : comparer les lectures logiques et le temps CPU avant/après indexation.';
