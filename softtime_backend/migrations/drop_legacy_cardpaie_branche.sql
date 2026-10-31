/* ============================================================================
   SoftTime - Suppression de la colonne legacy T_CARDPAIE.BRANCHE
   ----------------------------------------------------------------------------
   CONTEXTE
   La notion de "branche" est remplacée par "département" dans l'application.
   Le département est déjà stocké dans T_CARDPAIE.DEPARTEMENT (alimenté par la
   synchronisation SAGE) ; la colonne BRANCHE n'est plus lue ni écrite par le
   code applicatif.

   CE SCRIPT (a executer MANUELLEMENT, une seule fois, quand vous etes pret) :
     - supprime la colonne BRANCHE de T_CARDPAIE si elle existe.

   *** A LIRE AVANT D'EXECUTER ***
   - Faites une sauvegarde de la base avant execution, la suppression de
     colonne est irreversible.
   - Executez le script avec un compte ayant les droits ALTER.
   - Le script est idempotent : vous pouvez le relancer sans erreur.
   - Les valeurs BRANCHE ne sont PAS recopiées dans DEPARTEMENT : les deux
     notions n'ont pas la même sémantique. Si vous devez conserver ces valeurs,
     exportez-les avant (SELECT * INTO <table_de_sauvegarde> FROM ...).
   ============================================================================ */

SET NOCOUNT ON;
GO

PRINT '=== Suppression de la colonne legacy T_CARDPAIE.BRANCHE ===';
GO

IF COL_LENGTH(N'dbo.T_CARDPAIE', N'BRANCHE') IS NOT NULL
BEGIN
    ALTER TABLE dbo.T_CARDPAIE DROP COLUMN BRANCHE;
    PRINT '  -> T_CARDPAIE.BRANCHE supprimee.';
END
ELSE
BEGIN
    PRINT '  -> T_CARDPAIE.BRANCHE absente : rien a faire.';
END
GO
