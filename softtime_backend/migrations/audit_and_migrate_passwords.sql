-- =====================================================================================
-- Migration et Audit de Sécurité des Mots de Passe - SoftTime-WEB
-- =====================================================================================
-- Contexte :
-- Les versions antérieures de SoftTime stockaient les mots de passe des utilisateurs
-- en texte clair dans la colonne dbo.T_RESPONSABLE.PASSWORD (ou éventuellement sous d'anciennes
-- versions avec hachage ASP.NET Identity V2).
-- 
-- Le backend .NET 8 implémente désormais Microsoft.AspNetCore.Identity.PasswordHasher<TUser>
-- utilisant l'algorithme standard PBKDF2 (HMAC-SHA256, sel cryptographique de 128 bits,
-- 100 000 itérations, format Identity V3 - 84 caractères Base64, ou 124 caractères avec HMAC-SHA512).
-- 
-- La bibliothèque accepte également les hachages existants Identity V2 (68 caractères Base64),
-- qu'elle valide et migre automatiquement vers le format V3 dès la première connexion de l'utilisateur.
--
-- SÉPARATION STRICTE DDL / DML :
-- L'application n'exécute PLUS d'ALTER TABLE au démarrage (évite les verrous Sch-M et la perte
-- de contrainte NOT NULL). L'élargissement de schéma éventuel doit être réalisé par un DBA
-- via le script dédié : migrations/upgrade_password_column_schema.sql.
-- =====================================================================================

-- 1. CONTRÔLE DE LA LONGUEUR ET DE LA NULLABILITÉ RÉELLE DE LA COLONNE PASSWORD
-- Les hachages V3 nécessitent 84 caractères (124 pour SHA-512) et V2 68 caractères.
-- La colonne doit être en nvarchar(max) ou nvarchar(>= 128).
SELECT 
    TABLE_SCHEMA,
    TABLE_NAME,
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE,
    CASE 
        WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN 'CONFORME (nvarchar(max))'
        WHEN CHARACTER_MAXIMUM_LENGTH >= 128 THEN 'CONFORME (>= 128 caractères)'
        ELSE 'ATTENTION : Colonne trop courte, exécuter upgrade_password_column_schema.sql'
    END AS STATUT_SCHEMA
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' 
  AND TABLE_NAME = 'T_RESPONSABLE' 
  AND COLUMN_NAME = 'PASSWORD';

-- 2. DDL DE SÉCURISATION DU SCHÉMA (Exécutable par DBA)
-- ATTENTION : Pour une exécution sécurisée avec préservation de la nullabilité NOT NULL/NULL,
-- exécuter le script transactionnel dédié : migrations/upgrade_password_column_schema.sql.
-- Le snippet ci-dessous est un rappel idempotente :
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = 'dbo' 
      AND TABLE_NAME = 'T_RESPONSABLE' 
      AND COLUMN_NAME = 'PASSWORD'
      AND (CHARACTER_MAXIMUM_LENGTH > 0 AND CHARACTER_MAXIMUM_LENGTH < 128)
)
BEGIN
    PRINT N'ALERTE DBA : La colonne PASSWORD doit être mise à niveau via upgrade_password_column_schema.sql avant migration.';
END
ELSE
BEGIN
    PRINT N'La colonne PASSWORD est déjà correctement dimensionnée (>= 128 caractères).';
END;

-- 3. AUDIT DÉTAILLÉ DE L''ÉTAT DES MOTS DE PASSE
-- Identifie avec précision :
-- - Identity V3 standard (84 car.) ou SHA512 (124 car.)
-- - Identity V2 hérité (68 car.)
-- - Formats de hachage externes (BCrypt, Argon2, PBKDF2)
-- - Empreintes hexadécimales (MD5 = 32, SHA-1 = 40, SHA-256 = 64)
-- - Mots de passe en clair avérés
-- - Valeurs mal formées ou corrompues
SELECT 
    IDRESPONSABLE,
    IDROLE,
    LOGIN,
    NOMRESPONSABLE,
    EMAIL,
    LEN(PASSWORD) AS LONGUEUR_VALEUR,
    CASE 
        WHEN PASSWORD IS NULL OR LTRIM(RTRIM(PASSWORD)) = '' THEN 'VIDE'
        -- Identity V3 : 84 car. (HMAC-SHA256 ou HMAC-SHA512 sous-clé 32 octets) ou 124 car. (HMAC-SHA512 sous-clé 64 octets)
        WHEN (LEN(PASSWORD) = 84 OR LEN(PASSWORD) = 124 OR (LEN(PASSWORD) >= 68 AND LEN(PASSWORD) <= 160 AND LEN(PASSWORD) % 4 = 0)) AND (PASSWORD LIKE 'AQAA%') THEN 'HACHE_V3_PBKDF2'
        -- Identity V2 : 68 car. Base64
        WHEN LEN(PASSWORD) = 68 AND PASSWORD LIKE 'A%' AND (PASSWORD LIKE '%==' OR PASSWORD LIKE '%=') THEN 'HACHE_V2_LEGACY'
        -- Formats externes MCF
        WHEN PASSWORD LIKE '$2a$%' OR PASSWORD LIKE '$2b$%' OR PASSWORD LIKE '$argon2%' OR PASSWORD LIKE '$pbkdf2%' THEN 'FORMAT_INCONNU_MCF'
        -- Empreintes hexadécimales (MD5, SHA1, SHA256, SHA512)
        WHEN LEN(PASSWORD) IN (32, 40, 64, 128) AND PASSWORD NOT LIKE '%[^0-9a-fA-F]%' THEN 'FORMAT_INCONNU_HEX'
        -- Valeurs Base64 Identity altérées ou tronquées (commence par AQAA mais longueur invalide)
        WHEN PASSWORD LIKE 'AQAA%' AND (LEN(PASSWORD) < 68 OR LEN(PASSWORD) % 4 <> 0) THEN 'AMBIGU_OU_CORROMPU'
        ELSE 'EN_CLAIR_VULNERABLE'
    END AS STATUT_MOT_DE_PASSE,
    CASE 
        WHEN PASSWORD IS NULL OR LTRIM(RTRIM(PASSWORD)) = '' THEN 'Aucune action (mot de passe absent)'
        WHEN (LEN(PASSWORD) = 84 OR LEN(PASSWORD) = 124 OR (LEN(PASSWORD) >= 68 AND LEN(PASSWORD) <= 160 AND LEN(PASSWORD) % 4 = 0)) AND (PASSWORD LIKE 'AQAA%') THEN 'Conforme (aucun changement requis)'
        WHEN LEN(PASSWORD) = 68 AND PASSWORD LIKE 'A%' AND (PASSWORD LIKE '%==' OR PASSWORD LIKE '%=') THEN 'Compatible V2 (re-hachage transparent en V3 à la prochaine connexion)'
        WHEN PASSWORD LIKE '$2a$%' OR PASSWORD LIKE '$2b$%' OR PASSWORD LIKE '$argon2%' OR PASSWORD LIKE '$pbkdf2%' THEN 'PRÉSERVÉ INTACT (Réinitialisation administrative requise)'
        WHEN LEN(PASSWORD) IN (32, 40, 64, 128) AND PASSWORD NOT LIKE '%[^0-9a-fA-F]%' THEN 'PRÉSERVÉ INTACT (Ancien hash MD5/SHA, réinitialisation requise)'
        WHEN PASSWORD LIKE 'AQAA%' AND (LEN(PASSWORD) < 68 OR LEN(PASSWORD) % 4 <> 0) THEN 'PRÉSERVÉ INTACT (Valeur corrompue, réinitialisation requise)'
        ELSE 'MIGRATION REQUISE (sécurisé à la connexion ou via migration contrôlée)'
    END AS ACTION_RECOMMANDEE
FROM dbo.T_RESPONSABLE;

-- 4. RÉSUMÉ STATISTIQUE PAR FORMAT DE MOT DE PASSE
SELECT 
    CASE 
        WHEN PASSWORD IS NULL OR LTRIM(RTRIM(PASSWORD)) = '' THEN 'Mot de passe absent ou vide'
        WHEN (LEN(PASSWORD) = 84 OR LEN(PASSWORD) = 124 OR (LEN(PASSWORD) >= 68 AND LEN(PASSWORD) <= 160 AND LEN(PASSWORD) % 4 = 0)) AND (PASSWORD LIKE 'AQAA%') THEN 'Haché V3 (PBKDF2 HMAC-SHA256 ou HMAC-SHA512)'
        WHEN LEN(PASSWORD) = 68 AND PASSWORD LIKE 'A%' AND (PASSWORD LIKE '%==' OR PASSWORD LIKE '%=') THEN 'Haché V2 (Legacy HMAC-SHA1)'
        WHEN PASSWORD LIKE '$2a$%' OR PASSWORD LIKE '$2b$%' OR PASSWORD LIKE '$argon2%' OR PASSWORD LIKE '$pbkdf2%' THEN 'Format externe (MCF / BCrypt / Argon2)'
        WHEN LEN(PASSWORD) IN (32, 40, 64, 128) AND PASSWORD NOT LIKE '%[^0-9a-fA-F]%' THEN 'Empreinte hexadécimale (MD5 / SHA-1 / SHA-256)'
        WHEN PASSWORD LIKE 'AQAA%' AND (LEN(PASSWORD) < 68 OR LEN(PASSWORD) % 4 <> 0) THEN 'Valeur corrompue / tronquée'
        ELSE 'En clair / non haché (historique)'
    END AS Statut,
    COUNT(*) AS NombreComptes
FROM dbo.T_RESPONSABLE
GROUP BY 
    CASE 
        WHEN PASSWORD IS NULL OR LTRIM(RTRIM(PASSWORD)) = '' THEN 'Mot de passe absent ou vide'
        WHEN (LEN(PASSWORD) = 84 OR LEN(PASSWORD) = 124 OR (LEN(PASSWORD) >= 68 AND LEN(PASSWORD) <= 160 AND LEN(PASSWORD) % 4 = 0)) AND (PASSWORD LIKE 'AQAA%') THEN 'Haché V3 (PBKDF2 HMAC-SHA256 ou HMAC-SHA512)'
        WHEN LEN(PASSWORD) = 68 AND PASSWORD LIKE 'A%' AND (PASSWORD LIKE '%==' OR PASSWORD LIKE '%=') THEN 'Haché V2 (Legacy HMAC-SHA1)'
        WHEN PASSWORD LIKE '$2a$%' OR PASSWORD LIKE '$2b$%' OR PASSWORD LIKE '$argon2%' OR PASSWORD LIKE '$pbkdf2%' THEN 'Format externe (MCF / BCrypt / Argon2)'
        WHEN LEN(PASSWORD) IN (32, 40, 64, 128) AND PASSWORD NOT LIKE '%[^0-9a-fA-F]%' THEN 'Empreinte hexadécimale (MD5 / SHA-1 / SHA-256)'
        WHEN PASSWORD LIKE 'AQAA%' AND (LEN(PASSWORD) < 68 OR LEN(PASSWORD) % 4 <> 0) THEN 'Valeur corrompue / tronquée'
        ELSE 'En clair / non haché (historique)'
    END;

-- 5. VALIDATION POST-MIGRATION
-- Cette requête liste les comptes vulnérables en clair non encore sécurisés.
SELECT 
    IDRESPONSABLE, 
    LOGIN, 
    PASSWORD,
    'ALERTE : Compte en clair non migré' AS Diagnostic
FROM dbo.T_RESPONSABLE
WHERE PASSWORD IS NOT NULL 
  AND LTRIM(RTRIM(PASSWORD)) <> ''
  AND NOT (
      -- V3 conforme (84 car., 124 car. ou variante valide)
      ((LEN(PASSWORD) = 84 OR LEN(PASSWORD) = 124 OR (LEN(PASSWORD) >= 68 AND LEN(PASSWORD) <= 160 AND LEN(PASSWORD) % 4 = 0)) AND (PASSWORD LIKE 'AQAA%'))
      OR
      -- V2 conforme (68 car.)
      (LEN(PASSWORD) = 68 AND PASSWORD LIKE 'A%' AND (PASSWORD LIKE '%==' OR PASSWORD LIKE '%='))
      OR
      -- Format externe préservé
      (PASSWORD LIKE '$2a$%' OR PASSWORD LIKE '$2b$%' OR PASSWORD LIKE '$argon2%' OR PASSWORD LIKE '$pbkdf2%')
      OR
      -- Format hex préservé
      (LEN(PASSWORD) IN (32, 40, 64, 128) AND PASSWORD NOT LIKE '%[^0-9a-fA-F]%')
  );
