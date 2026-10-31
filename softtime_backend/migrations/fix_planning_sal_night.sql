/* =====================================================================================
   Réparation des plannings salariés existants (alignement desktop).

   À exécuter manuellement sur chaque base SOFTTIME, après sauvegarde :
     sqlcmd -S <serveur> -d <base> -i fix_planning_sal_night.sql

   Le script est idempotent. Il ne modifie pas le schéma : DateP reste datetime2, mais
   les valeurs sont normalisées à minuit. Le type date du desktop est une migration
   distincte.
   ===================================================================================== */

-- 1. DateP : toujours une date à minuit.
UPDATE dbo.T_PLANNING_SAL
SET DateP = CONVERT(date, DateP)
WHERE DateP IS NOT NULL
  AND CONVERT(time, DateP) <> CONVERT(time, '00:00:00');
GO

-- 2. Pause NULL -> 00:00:00, comme le desktop.
UPDATE dbo.T_PLANNING_SAL
SET Pause = CONVERT(time, '00:00:00')
WHERE Pause IS NULL;
GO

-- 3. OffP NULL -> 0.
UPDATE dbo.T_PLANNING_SAL
SET OffP = 0
WHERE OffP IS NULL;
GO

-- 4. Nuit stockée sur la même date : HD passe au lendemain.
--    Seules les lignes non OFF dont HD est antérieur à HA sur la même date sont concernées.
UPDATE dbo.T_PLANNING_SAL
SET HD = DATEADD(day, 1, HD)
WHERE ISNULL(OffP, 0) = 0
  AND HA IS NOT NULL
  AND HD IS NOT NULL
  AND CONVERT(date, HD) = CONVERT(date, HA)
  AND CONVERT(time, HD) < CONVERT(time, HA);
GO

-- Contrôle : lignes de nuit encore stockées sur la même date (doit renvoyer 0 ligne).
SELECT ID, IDSAL, NoSHIFT, DateP, HA, HD
FROM dbo.T_PLANNING_SAL
WHERE ISNULL(OffP, 0) = 0
  AND HA IS NOT NULL
  AND HD IS NOT NULL
  AND CONVERT(date, HD) = CONVERT(date, HA)
  AND CONVERT(time, HD) < CONVERT(time, HA);
GO
