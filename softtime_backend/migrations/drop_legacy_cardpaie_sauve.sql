/* ============================================================================
   SoftTime - Suppression de la table legacy T_CARDPAIE_SAUVE
   ----------------------------------------------------------------------------
   CONTEXTE
   Le desktop utilisait T_CARDPAIE_SAUVE comme instantané des correspondances
   SAGE <-> badge : ActiverCorrespondance recopiait T_CARDPAIE dedans, puis
   supprimait toute la chaîne de traitement (pointages, heures corrigées,
   anomalies, HS, planning, catégories, tolérances) ; DesactiverCorrespondance
   restaurait l'instantané.

   L'application web gère le mode correspondance manuelle de façon NON
   destructrice (T_ACTIVECORRESP par paire de bases : les cartes retirées par
   l'auto-mapping sont désactivées, jamais supprimées). La table n'est donc plus
   ni lue ni écrite, et son entité a été retirée du modèle EF.

   CE SCRIPT (a executer MANUELLEMENT, une seule fois, quand vous etes pret) :
     - supprime la table T_CARDPAIE_SAUVE si elle existe.

   *** A LIRE AVANT D'EXECUTER ***
   - Les données de cette table sont les anciens instantanés du desktop. Elles
     ne sont plus exploitables par l'application : si vous voulez les conserver,
     exportez-les avant (SELECT * INTO <table_de_sauvegarde> FROM ...).
   - Idempotent : relançable sans erreur.
   - Le script n'est PAS exécuté par l'application ; il est à lancer à la main.
   ============================================================================ */

SET NOCOUNT ON;
GO

PRINT '=== Suppression de la table legacy T_CARDPAIE_SAUVE ===';
GO

IF OBJECT_ID(N'dbo.T_CARDPAIE_SAUVE', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.T_CARDPAIE_SAUVE;
    PRINT '  -> T_CARDPAIE_SAUVE supprimee.';
END
ELSE
BEGIN
    PRINT '  -> T_CARDPAIE_SAUVE absente : rien a faire.';
END
GO
