/* ============================================================================
   SoftTime - Nettoyage des correspondances paie (T_CARDPAIE) en doublon
   ----------------------------------------------------------------------------
   CE QUE L'ON APPELLE "DOUBLON" ICI
   Un salarie ne doit avoir QU'UNE carte par base SAGE. Une carte est identifiee
   par (SAGE_BDD + matricule). L'ancienne version du script comparait les cartes
   dans une meme "paire" (serveur SAGE + base + serveur pointeuse + base) : elle
   ne voyait donc que 8 doublons alors que l'ecran en affiche beaucoup plus.

   POURQUOI IL Y A AUTANT DE DOUBLONS
   SyncCardPaieCoreAsync recherchait la carte existante dans le perimetre
   (SAGE_SERVEUR + SAGE_BDD + POINTEUSE_SERVEUR + POINTEUSE_BDD), alors que
   ListCardsAsync et ImportFromClockAsync raisonnent par base SAGE.
   Consequence : changer le libelle du serveur ("localhost" -> "localhost\\SQLEXPRESS")
   ou la base pointeuse recreait un jeu COMPLET de cartes pour les memes salaries.
   Le code est corrige (l'identite est desormais base SAGE + matricule, et la
   carte suit la configuration courante). Ce script nettoie l'existant.

   ORDRE D'EXECUTION
     1. Sauvegardez la base.
     2. PARTIE 1 : diagnostic (lecture seule) - mesurez.
     3. PARTIE 2 : nettoyage (transaction, decommenter le bloc). Pour chaque
        salarie il :
          a. repointe T_CAT_SAL (affectations), T_CATEGORIE (superviseur) et
             T_PLANNING_SAL (planning) vers la carte conservee,
          b. supprime les lignes devenues en double,
          c. supprime les cartes en double (uniquement ORIGINE = 'AUTO').
        Ces 3 tables referencent T_CARDPAIE.ID par cle etrangere : les repointer
        est obligatoire, sinon la suppression des cartes echoue (erreur 547).
        La carte conservee est, par ordre de priorite :
          - une carte manuelle (ORIGINE <> 'AUTO') plutot qu'une carte auto,
          - une carte dont la paire est encore configuree,
          - a defaut la plus recente (ID le plus grand).
     4. PARTIE 3 : controles apres nettoyage (la 1re grille doit etre vide).
     5. PARTIE 4 : badges portes par deux salaries differents (a corriger a la
        main dans l'ecran "Correspondances paie", aucune suppression ici).

   COMPATIBILITE
   Ecrit pour SQL Server 2008 et superieur (base_softtime est en niveau de
   compatibilite <= 100) : aucune fonction 2012+ (TRY_CONVERT, CONCAT, IIF,
   FIRST_VALUE). Le matricule est normalise par SUBSTRING/PATINDEX :
   "00369", "369" et " 00369 " donnent tous "369".
   ============================================================================ */

SET NOCOUNT ON;
GO

/* ----------------------------------------------------------------------------
   PARTIE 1 - DIAGNOSTIC (lecture seule)
   ---------------------------------------------------------------------------- */

PRINT N'--- 1.a Repartition des cartes par base SAGE / serveur / base pointeuse ---';
SELECT SAGE_BDD, SAGE_SERVEUR, POINTEUSE_BDD, COUNT(*) AS Cartes
FROM dbo.T_CARDPAIE
GROUP BY SAGE_BDD, SAGE_SERVEUR, POINTEUSE_BDD
ORDER BY SAGE_BDD, Cartes DESC;
GO

PRINT N'--- 1.b Nombre de doublons visibles (meme salarie dans la meme base SAGE) ---';
;WITH src AS (
    SELECT LTRIM(RTRIM(ISNULL(SAGE_BDD, N''))) AS Bdd,
           LTRIM(RTRIM(ISNULL(SAGE_MATRICULE, N''))) AS MatTrim
    FROM dbo.T_CARDPAIE
), norm AS (
    SELECT Bdd,
           UPPER(CASE WHEN LEN(MatTrim) = 0 THEN N''
                      ELSE SUBSTRING(MatTrim, PATINDEX('%[^0]%', MatTrim + N'.'), LEN(MatTrim))
                 END) AS MatKey
    FROM src
)
SELECT Bdd AS SAGE_BDD,
       COUNT(*) AS Cartes,
       COUNT(DISTINCT MatKey) AS SalariesDistincts,
       COUNT(*) - COUNT(DISTINCT MatKey) AS DoublonsVisibles,
       COUNT(*) - COUNT(DISTINCT MatKey) AS CartesASupprimer
FROM norm
WHERE MatKey <> N''
GROUP BY Bdd
HAVING COUNT(*) > COUNT(DISTINCT MatKey)
ORDER BY DoublonsVisibles DESC;
GO

/* ----------------------------------------------------------------------------
   PARTIE 2 - NETTOYAGE (decommenter le bloc entier, y compris BEGIN TRAN)
   Verifiez les comptes affiches avant de valider par COMMIT.
   ---------------------------------------------------------------------------- */

/*
IF OBJECT_ID('tempdb..#c') IS NOT NULL DROP TABLE #c;
IF OBJECT_ID('tempdb..#map') IS NOT NULL DROP TABLE #map;

-- 2.a  Normalisation : une ligne par carte, avec la cle de regroupement
SELECT c.ID,
       LTRIM(RTRIM(ISNULL(c.SAGE_BDD, N''))) AS Bdd,
       c.ORIGINE,
       CASE WHEN EXISTS (
                SELECT 1 FROM dbo.T_BDD_SAGE s
                WHERE ISNULL(LTRIM(RTRIM(s.SERVEUR)), N'') = LTRIM(RTRIM(ISNULL(c.SAGE_SERVEUR, N'')))
                  AND ISNULL(LTRIM(RTRIM(s.NOM_BD)),  N'') = LTRIM(RTRIM(ISNULL(c.SAGE_BDD, N''))))
            AND EXISTS (
                SELECT 1 FROM dbo.T_BDD_POINTEUSE p
                WHERE ISNULL(LTRIM(RTRIM(p.SERVEUR)), N'') = LTRIM(RTRIM(ISNULL(c.POINTEUSE_SERVEUR, N'')))
                  AND ISNULL(LTRIM(RTRIM(p.NOM_BD)),  N'') = LTRIM(RTRIM(ISNULL(c.POINTEUSE_BDD, N''))))
            THEN 0 ELSE 1 END AS PaireKO,
       UPPER(CASE WHEN LEN(LTRIM(RTRIM(ISNULL(c.SAGE_MATRICULE, N'')))) = 0 THEN N''
                  ELSE SUBSTRING(LTRIM(RTRIM(c.SAGE_MATRICULE)),
                                 PATINDEX('%[^0]%', LTRIM(RTRIM(c.SAGE_MATRICULE)) + N'.'),
                                 LEN(LTRIM(RTRIM(c.SAGE_MATRICULE))))
             END) AS MatKey
INTO #c
FROM dbo.T_CARDPAIE c;

-- un matricule vide n'est jamais dedoublonne
DELETE FROM #c WHERE MatKey = N'';

-- 2.b  Carte conservee pour chaque (base SAGE + matricule)
SELECT x.ID AS CardID,
       (SELECT TOP 1 y.ID
        FROM #c y
        WHERE y.Bdd = x.Bdd AND y.MatKey = x.MatKey
        ORDER BY CASE WHEN y.ORIGINE = 'AUTO' THEN 1 ELSE 0 END, y.PaireKO, y.ID DESC) AS SurvivantID
INTO #map
FROM #c x;

-- 2.c  Controles AVANT modification
SELECT COUNT(*) AS CartesEnDouble FROM #map WHERE CardID <> SurvivantID;
SELECT COUNT(*) AS AffectationsARepointer
FROM dbo.T_CAT_SAL a
INNER JOIN #map m ON m.CardID = a.MATRICULE_SAGE
WHERE m.CardID <> m.SurvivantID;
SELECT COUNT(*) AS CategoriesARepointer
FROM dbo.T_CATEGORIE g
INNER JOIN #map m ON m.CardID = g.ID_CARDPAIE
WHERE m.CardID <> m.SurvivantID;
SELECT COUNT(*) AS LignesPlanningARepointer
FROM dbo.T_PLANNING_SAL p
INNER JOIN #map m ON m.CardID = p.IDSAL
WHERE m.CardID <> m.SurvivantID;
SELECT COUNT(*) AS CartesManuellesEnDouble
FROM #map m
INNER JOIN dbo.T_CARDPAIE c ON c.ID = m.CardID
WHERE m.CardID <> m.SurvivantID AND c.ORIGINE <> 'AUTO';

BEGIN TRAN;

-- 2.d  T_CAT_SAL (affectations) : MATRICULE_SAGE contient l'ID de la carte
UPDATE a
SET a.MATRICULE_SAGE = m.SurvivantID
FROM dbo.T_CAT_SAL a
INNER JOIN #map m ON m.CardID = a.MATRICULE_SAGE
WHERE m.CardID <> m.SurvivantID AND m.SurvivantID IS NOT NULL;
PRINT N'Affectations repointees : ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- 2.e  Affectations devenues en double (meme carte + meme categorie)
DELETE a
FROM dbo.T_CAT_SAL a
WHERE EXISTS (SELECT 1 FROM dbo.T_CAT_SAL b
              WHERE b.MATRICULE_SAGE = a.MATRICULE_SAGE
                AND b.ID_CATEG = a.ID_CATEG
                AND b.ID < a.ID);
PRINT N'Affectations en double supprimees : ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- 2.f  T_CATEGORIE : ID_CARDPAIE = carte du superviseur (cle etrangere)
UPDATE g
SET g.ID_CARDPAIE = m.SurvivantID
FROM dbo.T_CATEGORIE g
INNER JOIN #map m ON m.CardID = g.ID_CARDPAIE
WHERE m.CardID <> m.SurvivantID AND m.SurvivantID IS NOT NULL;
PRINT N'Categories repointees : ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- 2.g  T_PLANNING_SAL : IDSAL = carte du salarie (cle etrangere)
UPDATE p
SET p.IDSAL = m.SurvivantID
FROM dbo.T_PLANNING_SAL p
INNER JOIN #map m ON m.CardID = p.IDSAL
WHERE m.CardID <> m.SurvivantID AND m.SurvivantID IS NOT NULL;
PRINT N'Lignes de planning repointees : ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- puis suppression des lignes de planning devenues identiques (meme carte, meme date, meme shift)
DELETE p
FROM dbo.T_PLANNING_SAL p
WHERE EXISTS (SELECT 1 FROM dbo.T_PLANNING_SAL q
              WHERE q.IDSAL = p.IDSAL
                AND ISNULL(q.DateP, '19000101') = ISNULL(p.DateP, '19000101')
                AND ISNULL(q.NoSHIFT, -1) = ISNULL(p.NoSHIFT, -1)
                AND q.ID < p.ID);
PRINT N'Lignes de planning en double supprimees : ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- 2.h  Cartes en double (les cartes manuelles ne sont jamais supprimees)
DELETE c
FROM dbo.T_CARDPAIE c
INNER JOIN #map m ON m.CardID = c.ID
WHERE m.CardID <> m.SurvivantID AND c.ORIGINE = 'AUTO';
PRINT N'Cartes supprimees : ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- 2.i  Verification : aucune ligne ne doit plus referencer une carte inexistante
SELECT (SELECT COUNT(*) FROM dbo.T_CAT_SAL a WHERE NOT EXISTS (SELECT 1 FROM dbo.T_CARDPAIE c WHERE c.ID = a.MATRICULE_SAGE)) AS AffectationsOrphelines,
       (SELECT COUNT(*) FROM dbo.T_CATEGORIE g WHERE NOT EXISTS (SELECT 1 FROM dbo.T_CARDPAIE c WHERE c.ID = g.ID_CARDPAIE)) AS CategoriesOrphelines,
       (SELECT COUNT(*) FROM dbo.T_PLANNING_SAL p WHERE NOT EXISTS (SELECT 1 FROM dbo.T_CARDPAIE c WHERE c.ID = p.IDSAL)) AS PlanningOrphelin;

-- COMMIT TRAN;   -- validez apres verification des comptes ci-dessus
-- ROLLBACK TRAN; -- en cas de doute

IF OBJECT_ID('tempdb..#c') IS NOT NULL DROP TABLE #c;
IF OBJECT_ID('tempdb..#map') IS NOT NULL DROP TABLE #map;
*/
GO

/* ----------------------------------------------------------------------------
   PARTIE 3 - CONTROLES APRES NETTOYAGE (a relancer apres un COMMIT)
   Doublons restants par base SAGE (doit etre vide), puis doublons stricts dans
   une meme paire.
   ---------------------------------------------------------------------------- */

;WITH src AS (
    SELECT LTRIM(RTRIM(ISNULL(SAGE_BDD, N''))) AS Bdd,
           LTRIM(RTRIM(ISNULL(SAGE_MATRICULE, N''))) AS MatTrim
    FROM dbo.T_CARDPAIE
), norm AS (
    SELECT Bdd,
           UPPER(CASE WHEN LEN(MatTrim) = 0 THEN N''
                      ELSE SUBSTRING(MatTrim, PATINDEX('%[^0]%', MatTrim + N'.'), LEN(MatTrim))
                 END) AS MatKey
    FROM src
)
SELECT Bdd AS SAGE_BDD, COUNT(*) AS Cartes, COUNT(DISTINCT MatKey) AS SalariesDistincts
FROM norm
WHERE MatKey <> N''
GROUP BY Bdd
HAVING COUNT(*) > COUNT(DISTINCT MatKey);
GO

PRINT N'--- 3.b Cartes orphelines restantes (paire serveur/base plus configuree) ---';
;WITH src AS (
    SELECT CASE WHEN (NOT EXISTS (
                    SELECT 1 FROM dbo.T_BDD_SAGE s
                    WHERE ISNULL(LTRIM(RTRIM(s.SERVEUR)), N'') = ISNULL(LTRIM(RTRIM(c.SAGE_SERVEUR)), N'')
                      AND ISNULL(LTRIM(RTRIM(s.NOM_BD)),  N'') = ISNULL(LTRIM(RTRIM(c.SAGE_BDD)), N''))
                OR NOT EXISTS (
                    SELECT 1 FROM dbo.T_BDD_POINTEUSE p
                    WHERE ISNULL(LTRIM(RTRIM(p.SERVEUR)), N'') = ISNULL(LTRIM(RTRIM(c.POINTEUSE_SERVEUR)), N'')
                      AND ISNULL(LTRIM(RTRIM(p.NOM_BD)),  N'') = ISNULL(LTRIM(RTRIM(c.POINTEUSE_BDD)), N'')))
                THEN 1 ELSE 0 END AS EstOrpheline
    FROM dbo.T_CARDPAIE c
)
SELECT SUM(EstOrpheline) AS CartesOrphelines
FROM src;
GO

/* ----------------------------------------------------------------------------
   PARTIE 4 - DIAGNOSTIC : un meme badge porte par DEUX salaries differents
   (donnee ambigue : c'est ce qui fait que l'import des pointages ignore ces
   badges. A corriger dans l'ecran "Correspondances paie".)
   ---------------------------------------------------------------------------- */

;WITH src AS (
    SELECT LTRIM(RTRIM(ISNULL(SAGE_BDD, N''))) AS Bdd, POINTEUSE_NUMERO,
           LTRIM(RTRIM(ISNULL(SAGE_MATRICULE, N''))) AS MatTrim
    FROM dbo.T_CARDPAIE
    WHERE LTRIM(RTRIM(ISNULL(POINTEUSE_NUMERO, N''))) <> N''
), norm AS (
    SELECT Bdd, POINTEUSE_NUMERO,
           UPPER(CASE WHEN LEN(MatTrim) = 0 THEN N''
                      ELSE SUBSTRING(MatTrim, PATINDEX('%[^0]%', MatTrim + N'.'), LEN(MatTrim))
                 END) AS MatKey
    FROM src
)
SELECT Bdd AS SAGE_BDD, POINTEUSE_NUMERO, COUNT(*) AS NbCartes,
       COUNT(DISTINCT MatKey) AS NbMatricules
FROM norm
GROUP BY Bdd, POINTEUSE_NUMERO
HAVING COUNT(DISTINCT MatKey) > 1
ORDER BY NbCartes DESC;
GO
