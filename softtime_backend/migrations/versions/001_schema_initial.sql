/* =====================================================================================
   SoftTime — Migration 001 : schéma initial REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo). Le script s'exécute dans la base
             COURANTE : il ne crée pas de base et ne contient aucun USE.
   Sources : migrations/create_softtime_database.sql (inchangé), les méthodes Ensure* du
             démarrage (SageDatabaseConfiguration, PunchProcessingConfiguration,
             HistoryArchiveConfiguration, UserAccountConfiguration, RolePermissionConfiguration),
             migrations/create_role_permission_table.sql et les index de pagination de
             migrations/add_pagination_performance_indexes.sql.

   Règles :
     - chaque objet est créé seulement s'il n'existe pas ; aucune instruction destructive ;
     - sur une base NEUVE (T_ROLE absente au début du script), tout le schéma est créé,
       index de pagination et clé étrangère compris ;
     - sur une base EXISTANTE, le script ajoute seulement les tables manquantes (avec leurs
       index), les colonnes ajoutées par les Ensure* et les données de départ dans les tables
       VIDES. Il ne construit aucun index sur une table existante (verrous, colonnes
       nvarchar(max) non indexables) : utilisez pour cela les scripts manuels
       add_pagination_performance_indexes.sql et add_performance_indexes.sql ;
     - AUCUN compte utilisateur n'est créé : le premier compte se crée par
       GET /api/auth/is-first-run puis POST /api/auth/create-admin ;
     - le schéma ml et la table __EFMigrationsHistory ne sont pas touchés (migrations EF).

   Exécution : automatique au démarrage si Database:AutoMigrate = true (voir README.md),
   ou manuelle : sqlcmd -S <instance> -E -C -d <base> -i 001_schema_initial.sql
   ===================================================================================== */

SET NOCOUNT ON;

-- Ces options sont REQUISES pour créer les index uniques FILTRÉS (UX_SEM_*).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

-- Base neuve ou existante ? Mémorisé pour la session (les lots suivants le relisent).
DECLARE @base_neuve int = CASE WHEN OBJECT_ID(N'dbo.T_ROLE', N'U') IS NULL THEN 1 ELSE 0 END;
EXEC sys.sp_set_session_context @key = N'softtime_001_base_neuve', @value = @base_neuve;
GO

/* =====================================================================================
   1. Annuaire / droits / authentification
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_ROLE', N'U') IS NULL
CREATE TABLE dbo.T_ROLE (
    IDROLE   decimal(18,0) IDENTITY(1,1) NOT NULL,
    NOMROLE  nvarchar(max) NOT NULL,
    CONSTRAINT PK_T_ROLE PRIMARY KEY (IDROLE)
);
GO

IF OBJECT_ID(N'dbo.T_RESPONSABLE', N'U') IS NULL
CREATE TABLE dbo.T_RESPONSABLE (
    IDRESPONSABLE   decimal(18,0) IDENTITY(1,1) NOT NULL,
    IDROLE          decimal(18,0) NOT NULL,
    NOMRESPONSABLE  nvarchar(max) NULL,
    LOGIN           nvarchar(max) NULL,
    PASSWORD        nvarchar(max) NULL,
    MATRICULE       nvarchar(max) NULL,
    EMAIL           nvarchar(200) NULL,
    CONSTRAINT PK_T_RESPONSABLE PRIMARY KEY (IDRESPONSABLE)
);
GO

IF OBJECT_ID(N'dbo.T_DROIT', N'U') IS NULL
CREATE TABLE dbo.T_DROIT (
    IDDROIT  decimal(18,0) IDENTITY(1,1) NOT NULL,
    DROIT    nvarchar(max) NOT NULL,
    CONSTRAINT PK_T_DROIT PRIMARY KEY (IDDROIT)
);
GO

IF OBJECT_ID(N'dbo.T_DROIT_ROLE', N'U') IS NULL
CREATE TABLE dbo.T_DROIT_ROLE (
    IDDROITROLE  decimal(18,0) IDENTITY(1,1) NOT NULL,
    IDDROIT      decimal(18,0) NOT NULL,
    IDROLE       decimal(18,0) NOT NULL,
    CONSTRAINT PK_T_DROIT_ROLE PRIMARY KEY (IDDROITROLE)
);
GO

IF OBJECT_ID(N'dbo.T_BDD_AUTORISE', N'U') IS NULL
CREATE TABLE dbo.T_BDD_AUTORISE (
    IDBDDAUTORISE  int           IDENTITY(1,1) NOT NULL,
    IDRESPONSABLE  decimal(18,2) NOT NULL,
    IDBDD          int           NOT NULL,
    CONSTRAINT PK_T_BDD_AUTORISE PRIMARY KEY (IDBDDAUTORISE)
);
GO

IF OBJECT_ID(N'dbo.T_USER_PERMISSION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_USER_PERMISSION (
        ID              int           IDENTITY(1,1) NOT NULL,
        IDRESPONSABLE   decimal(18,0) NOT NULL,
        PERMISSION_CODE nvarchar(128) NOT NULL,
        IS_GRANTED      bit           NOT NULL CONSTRAINT DF_T_USER_PERMISSION_IS_GRANTED DEFAULT (1),
        CONSTRAINT PK_T_USER_PERMISSION PRIMARY KEY (ID)
    );
    CREATE UNIQUE INDEX UX_T_USER_PERMISSION ON dbo.T_USER_PERMISSION (IDRESPONSABLE, PERMISSION_CODE);
END;
GO

/* =====================================================================================
   2. Connexions sources (Sage / Pointeuse)
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_BDD_SAGE', N'U') IS NULL
CREATE TABLE dbo.T_BDD_SAGE (
    ID                       int           IDENTITY(1,1) NOT NULL,
    SERVEUR                  nvarchar(max) NULL,
    TLOGIN                   nvarchar(max) NULL,
    TMDP                     nvarchar(max) NULL,
    TYPE_AUTH                bit           NULL,
    NOM_BD                   nvarchar(max) NULL,
    TYPE_BASE                nvarchar(max) NOT NULL CONSTRAINT DF_T_BDD_SAGE_TYPE_BASE DEFAULT (N'STANDARD'),
    MAP_TABLE                nvarchar(max) NULL,
    MAP_COL_MATRICULE        nvarchar(max) NULL,
    MAP_COL_NOM              nvarchar(max) NULL,
    MAP_COL_PRENOM           nvarchar(max) NULL,
    MAP_COL_BADGE            nvarchar(max) NULL,
    MAP_COL_DEPARTEMENT      nvarchar(max) NULL,
    MAP_COL_SERVICE          nvarchar(max) NULL,
    MAP_COL_CODE_DEPARTEMENT nvarchar(max) NULL,
    MAP_TABLE_CODE_CONSTANTE nvarchar(128) NULL,
    MAP_COL_CODE_CONSTANTE   nvarchar(128) NULL,
    CONSTRAINT PK_T_BDD_SAGE PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_BDD_POINTEUSE', N'U') IS NULL
CREATE TABLE dbo.T_BDD_POINTEUSE (
    ID                    int           IDENTITY(1,1) NOT NULL,
    SERVEUR               nvarchar(max) NULL,
    TLOGIN                nvarchar(max) NULL,
    TMDP                  nvarchar(max) NULL,
    TYPE_AUTH             bit           NULL,
    NOM_BD                nvarchar(max) NULL,
    TYPE_POINTAGE         nvarchar(max) NULL,
    ACTIVE                bit           NULL,
    TYPE_BASE             nvarchar(max) NOT NULL CONSTRAINT DF_T_BDD_POINTEUSE_TYPE_BASE DEFAULT (N'STANDARD'),
    MAP_USER_TABLE        nvarchar(max) NULL,
    MAP_USER_COL_ID       nvarchar(max) NULL,
    MAP_USER_COL_BADGE    nvarchar(max) NULL,
    MAP_USER_COL_SSN      nvarchar(max) NULL,
    MAP_USER_COL_NOM      nvarchar(max) NULL,
    MAP_PUNCH_TABLE       nvarchar(max) NULL,
    MAP_PUNCH_COL_USER_ID nvarchar(max) NULL,
    MAP_PUNCH_COL_DATETIME nvarchar(max) NULL,
    MAP_PUNCH_COL_TYPE    nvarchar(max) NULL,
    CONSTRAINT PK_T_BDD_POINTEUSE PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   3. Cartes salariés / catégories / affectations / tolérances
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_CARDPAIE', N'U') IS NULL
CREATE TABLE dbo.T_CARDPAIE (
    ID                int           IDENTITY(1,1) NOT NULL,
    SAGE_MATRICULE    nvarchar(64)  NULL,
    SAGE_NOM          nvarchar(max) NULL,
    SAGE_PRENOM       nvarchar(max) NULL,
    POINTEUSE_NUMERO  nvarchar(max) NULL,
    POINTEUSE_NOM     nvarchar(max) NULL,
    SAGE_SERVEUR      nvarchar(max) NULL,
    POINTEUSE_SERVEUR nvarchar(max) NULL,
    SAGE_BDD          nvarchar(128) NULL,
    POINTEUSE_BDD     nvarchar(max) NULL,
    DATE              datetime2     NULL,
    ORIGINE           nvarchar(max) NOT NULL CONSTRAINT DF_T_CARDPAIE_ORIGINE DEFAULT (N'AUTO'),
    ACTIF             bit           NOT NULL CONSTRAINT DF_T_CARDPAIE_ACTIF DEFAULT (1),
    DEPARTEMENT       nvarchar(max) NULL,
    CONSTRAINT PK_T_CARDPAIE PRIMARY KEY (ID)
);
GO

-- T_CARDPAIE_SAUVE (instantane des correspondances du desktop) n'est PAS creee :
-- le mode correspondance de l'application web est non destructif, la table n'est plus mappee.
-- Pour la retirer d'une base existante : migrations/drop_legacy_cardpaie_sauve.sql
GO

IF OBJECT_ID(N'dbo.T_CATEGORIE', N'U') IS NULL
CREATE TABLE dbo.T_CATEGORIE (
    IDCATEGORIE  int           IDENTITY(1,1) NOT NULL,
    INTITULE     nvarchar(max) NULL,
    ID_CARDPAIE  int           NOT NULL,
    PAUSE        time          NULL,
    HEURESEMAINE decimal(18,2) NULL,
    BDD_SAGE     nvarchar(max) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    CONSTRAINT PK_T_CATEGORIE PRIMARY KEY (IDCATEGORIE)
);
GO

IF OBJECT_ID(N'dbo.T_CAT_SAL', N'U') IS NULL
CREATE TABLE dbo.T_CAT_SAL (
    ID            int           IDENTITY(1,1) NOT NULL,
    MATRICULE_SAGE int          NOT NULL,   -- FK logique vers T_CARDPAIE.ID
    ID_CATEG      int           NOT NULL,   -- FK logique vers T_CATEGORIE.IDCATEGORIE
    BDD_SAGE      nvarchar(max) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    CONSTRAINT PK_T_CAT_SAL PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_TOLERANCES', N'U') IS NULL
CREATE TABLE dbo.T_TOLERANCES (
    ID          int IDENTITY(1,1) NOT NULL,
    IDCATEGORIE int NOT NULL,
    TOLERANCE   int NOT NULL,
    TYPES_TOL   bit NOT NULL,
    SORTIE      int NULL,
    CONSTRAINT PK_T_TOLERANCES PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   4. Pointages / heures / anomalies
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_POINTAGE', N'U') IS NULL
CREATE TABLE dbo.T_POINTAGE (
    IDPOINTAGE         decimal(18,0) IDENTITY(1,1) NOT NULL,  -- IDENTITY ⇒ échelle 0 obligatoire
    NOM_BDD_SAGE       nvarchar(128) NULL,
    MATRICULE_SAGE     nvarchar(64)  NULL,
    DATE_POINTAGE      datetime2     NULL,
    HEURE_POINTAGE     time          NULL,
    DATE_IMPORTATION   datetime2     NULL,
    NOM_BDD_POINTEUSE  nvarchar(max) NULL,
    IDUNIQUE_POINTAGE  decimal(18,2) NULL,
    TYPE_POINTAGE      nvarchar(max) NULL,
    CONSTRAINT PK_T_POINTAGE PRIMARY KEY (IDPOINTAGE)
);
GO

IF OBJECT_ID(N'dbo.T_HEURECORRIGER', N'U') IS NULL
CREATE TABLE dbo.T_HEURECORRIGER (
    ID                    decimal(18,0) IDENTITY(1,1) NOT NULL,  -- IDENTITY ⇒ échelle 0 obligatoire
    BDD_SAGE              nvarchar(128) NULL,
    BDD_POINTEUSE         nvarchar(max) NULL,
    MATRICULE_SAGE        nvarchar(64)  NULL,
    DATE_POINTAGE_IN      datetime2     NULL,
    DATE_POINTAGE_OUT     datetime2     NULL,
    HENTREE               time          NULL,
    HPS                   time          NULL,
    HPE                   time          NULL,
    HSORTIE               time          NULL,
    ABSENCE_PM            bit           NULL,
    ABSENCE_AM            bit           NULL,
    INTITULE_ABSENCE      nvarchar(max) NULL,
    RETARD                time          NULL,
    FERIER                bit           NULL,
    FERIER_AM             bit           NULL,
    FERIER_PM             bit           NULL,
    IMPORTATION_SAGE      bit           NULL,
    DATE_IMPORTATION_SAGE datetime2     NULL,
    VALIDER_CORRECTION    bit           NULL,
    HS                    decimal(18,2) NULL,
    M_NUIT                decimal(18,2) NULL,
    M_DIMANCHE            decimal(18,2) NULL,
    M_FERIES              decimal(18,2) NULL,
    VALIDER_HS            bit           NULL,
    ORIGINE               nvarchar(10)  NULL,  -- AUTO (pré-traitement) | MANUEL (saisie/import) | NULL (hérité)
    CONSTRAINT PK_T_HEURECORRIGER PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_HEUREANOMALIE', N'U') IS NULL
CREATE TABLE dbo.T_HEUREANOMALIE (
    ID               decimal(18,0) IDENTITY(1,1) NOT NULL,  -- IDENTITY ⇒ échelle 0 obligatoire
    MATRICULE_SAGE   nvarchar(64)  NULL,
    DATE_POINTAGE_IN datetime2     NULL,
    DATE_POINTAGE_OUT datetime2    NULL,
    H1               time          NULL,
    H2               time          NULL,
    BDD_SAGE         nvarchar(128) NULL,
    BDD_POINTEUSE    nvarchar(max) NULL,
    HENTREE          time          NULL,
    HSORTIE          time          NULL,
    ABS_AM           bit           NULL,
    HPE              time          NULL,
    HPS              time          NULL,
    FERIES_AM        bit           NULL,
    ABS_PM           bit           NULL,
    FERIES_PM        bit           NULL,
    FERIES           bit           NULL,
    INTITULE_ABSENCE nvarchar(max) NULL,
    CONSTRAINT PK_T_HEUREANOMALIE PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_HEUREDIMANCHE', N'U') IS NULL
CREATE TABLE dbo.T_HEUREDIMANCHE (
    ID            int           IDENTITY(1,1) NOT NULL,
    BDD_SAGE      nvarchar(128) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    MATRICULE_SAGE nvarchar(64) NULL,
    Date_Dim      datetime2     NULL,
    HeureDebut    time          NULL,
    HeureFin      time          NULL,
    Valeur        decimal(18,2) NULL,
    CONSTRAINT PK_T_HEUREDIMANCHE PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   5. Heures supplémentaires (traitement GTA)
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_HS_VALIDER', N'U') IS NULL
CREATE TABLE dbo.T_HS_VALIDER (
    ID            int           IDENTITY(1,1) NOT NULL,
    BDD_SAGE      nvarchar(128) NULL,
    BDD_POINTAGE  nvarchar(max) NULL,
    MATRICULES    nvarchar(64)  NULL,
    DATE_DEB      datetime2     NULL,
    DATE_FIN      datetime2     NULL,
    HT            decimal(18,2) NULL,
    HS            decimal(18,2) NULL,
    NUIT          decimal(18,2) NULL,
    DIMANCHE      decimal(18,2) NULL,
    FERIES        decimal(18,2) NULL,
    VALIDATION_HS bit           NULL,
    RETARD        decimal(18,2) NULL,
    ABSENCE       decimal(18,2) NULL,
    CONSTRAINT PK_T_HS_VALIDER PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_HSExoImp', N'U') IS NULL
CREATE TABLE dbo.T_HSExoImp (
    OID           int           IDENTITY(1,1) NOT NULL,
    Matricule     nvarchar(64)  NULL,
    NumSal        int           NULL,
    EXO130        decimal(18,2) NULL,
    EXO150        decimal(18,2) NULL,
    I130          decimal(18,2) NULL,
    I150          decimal(18,2) NULL,
    PeriodeDebut  datetime2     NULL,
    PeriodeFin    datetime2     NULL,
    FERIE         decimal(18,2) NULL,
    NUIT          decimal(18,2) NULL,
    DIM           decimal(18,2) NULL,
    RETARD        decimal(18,2) NULL,
    ABSENCE       decimal(18,2) NULL,
    BDD_SAGE      nvarchar(128) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    CONSTRAINT PK_T_HSExoImp PRIMARY KEY (OID)
);
GO

IF OBJECT_ID(N'dbo.T_HSSemaine', N'U') IS NULL
CREATE TABLE dbo.T_HSSemaine (
    OID           int           IDENTITY(1,1) NOT NULL,
    Matricule     nvarchar(64)  NULL,
    SalNum        int           NULL,
    TotalHeure    decimal(18,2) NULL,
    HS            decimal(18,2) NULL,
    HS1           decimal(18,2) NULL,
    HS2           decimal(18,2) NULL,
    PeriodeDebut  datetime2     NULL,
    PeriodeFin    datetime2     NULL,
    Semaine       int           NULL,
    BDD_SAGE      nvarchar(128) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    CONSTRAINT PK_T_HSSemaine PRIMARY KEY (OID)
);
GO

IF OBJECT_ID(N'dbo.T_HSSAL', N'U') IS NULL
CREATE TABLE dbo.T_HSSAL (
    ID    int           IDENTITY(1,1) NOT NULL,
    IDSAL int           NULL,
    [Date] datetime2    NULL,
    HT    decimal(18,2) NULL,
    CONSTRAINT PK_T_HSSAL PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_REST_FERIE', N'U') IS NULL
CREATE TABLE dbo.T_REST_FERIE (
    ID                   int           IDENTITY(1,1) NOT NULL,
    BDD_SAGE             nvarchar(max) NULL,
    BDD_POINTEUSE        nvarchar(max) NULL,
    MATRICULE_SAGE       nvarchar(max) NULL,
    DATE_DEBUT_SEMAINE   datetime2     NULL,
    FERIE_DEBUT_SEMAINE  decimal(18,2) NULL,
    ETAT                 bit           NULL,
    CONSTRAINT PK_T_REST_FERIE PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_MAJORATION', N'U') IS NULL
CREATE TABLE dbo.T_MAJORATION (
    ID            int           IDENTITY(1,1) NOT NULL,
    Mojoration    nvarchar(max) NULL,
    Cotation      decimal(18,2) NULL,
    BDD_SAGE      nvarchar(max) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    CONSTRAINT PK_T_MAJORATION PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   6. Planning
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_PLANNING', N'U') IS NULL
CREATE TABLE dbo.T_PLANNING (
    IDPLANNING decimal(18,0) IDENTITY(1,1) NOT NULL,  -- IDENTITY ⇒ échelle 0 obligatoire
    NoSHIFT    int       NULL,
    HA         time      NULL,
    PAUSE      time      NULL,
    HD         time      NULL,
    Intitule   nvarchar(max) NULL,
    CONSTRAINT PK_T_PLANNING PRIMARY KEY (IDPLANNING)
);
GO

IF OBJECT_ID(N'dbo.T_PLANNING_SAL', N'U') IS NULL
CREATE TABLE dbo.T_PLANNING_SAL (
    ID            int       IDENTITY(1,1) NOT NULL,
    IDSAL         int       NOT NULL,   -- FK logique vers T_CARDPAIE.ID
    NoSHIFT       int       NULL,
    DateP         datetime2 NULL,
    HA            datetime2 NULL,
    [Pause]       time      NULL,
    HD            datetime2 NULL,
    OffP          bit       NULL,
    BDD_SAGE      nvarchar(128) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    CONSTRAINT PK_T_PLANNING_SAL PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   7. Calendrier / fériés / absences / config horloge
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_FERIE', N'U') IS NULL
CREATE TABLE dbo.T_FERIE (
    ID       int           IDENTITY(1,1) NOT NULL,
    INTITULE nvarchar(max) NULL,
    [DATE]   datetime2     NULL,
    BDD_SAGE nvarchar(max) NULL,
    CONSTRAINT PK_T_FERIE PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_CODEABSENCE', N'U') IS NULL
CREATE TABLE dbo.T_CODEABSENCE (
    ID_ABSENCE       int           IDENTITY(1,1) NOT NULL,
    INTITULE_ABSENCE nvarchar(max) NULL,
    NOT_PAY          bit           NULL,
    CONSTRAINT PK_T_CODEABSENCE PRIMARY KEY (ID_ABSENCE)
);
GO

IF OBJECT_ID(N'dbo.T_CLOCK', N'U') IS NULL
CREATE TABLE dbo.T_CLOCK (
    IDPOINT    int IDENTITY(1,1) NOT NULL,
    MULTIPOINT bit NULL,
    CONSTRAINT PK_T_CLOCK PRIMARY KEY (IDPOINT)
);
GO

IF OBJECT_ID(N'dbo.T_ACTIVECORRESP', N'U') IS NULL
CREATE TABLE dbo.T_ACTIVECORRESP (
    ID                int       IDENTITY(1,1) NOT NULL,
    Active            bit       NULL,
    SAGE_SERVEUR      nvarchar(max) NULL,
    SAGE_BDD          nvarchar(max) NULL,
    POINTEUSE_SERVEUR nvarchar(max) NULL,
    POINTEUSE_BDD     nvarchar(max) NULL,
    DERNIERE_SYNC     datetime2 NULL,
    DATE_MODIF        datetime2 NULL,
    CONSTRAINT PK_T_ACTIVECORRESP PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   8. Semaines
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_Semaine', N'U') IS NULL
CREATE TABLE dbo.T_Semaine (
    CodeSemain nvarchar(max) NULL,
    DateDeb    datetime2     NULL,
    DateFin    datetime2     NULL
    -- Entité SANS clé (HasNoKey) : pas de PK, volontairement.
);
GO

IF OBJECT_ID(N'dbo.T_SemainePeriode', N'U') IS NULL
CREATE TABLE dbo.T_SemainePeriode (
    OID         int       IDENTITY(1,1) NOT NULL,
    NoSemaine   int       NULL,
    DebutSemaine datetime2 NULL,
    FinSemaine  datetime2 NULL,
    CONSTRAINT PK_T_SemainePeriode PRIMARY KEY (OID)
);
GO

/* =====================================================================================
   9. Impression / états (tables de travail alimentées par les rapports)
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_impr_ABS', N'U') IS NULL
CREATE TABLE dbo.T_impr_ABS (
    ID            int           IDENTITY(1,1) NOT NULL,
    BDD_SAGE      nvarchar(max) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    MATRICULES    nvarchar(max) NULL,
    DATE_POINTAGE nvarchar(max) NULL,
    ABSENCE_AM    bit           NULL,
    ABSENCE_PM    bit           NULL,
    INTITULE_ABS  nvarchar(max) NULL,
    CONSTRAINT PK_T_impr_ABS PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_impr_RET', N'U') IS NULL
CREATE TABLE dbo.T_impr_RET (
    ID            int           IDENTITY(1,1) NOT NULL,
    BDD_SAGE      nvarchar(max) NULL,
    BDD_POINTEUSE nvarchar(max) NULL,
    MATRICULES    nvarchar(max) NULL,
    DATE_POINTAGE nvarchar(max) NULL,
    HEURE_ENTREE  time          NULL,
    RETARDS       time          NULL,
    CONSTRAINT PK_T_impr_RET PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_impr_HS', N'U') IS NULL
CREATE TABLE dbo.T_impr_HS (
    ID          int           IDENTITY(1,1) NOT NULL,
    MATR        nvarchar(max) NULL,
    EXO130      nvarchar(max) NULL,
    EXO150      nvarchar(max) NULL,
    IMPO130     nvarchar(max) NULL,
    IMPO150     nvarchar(max) NULL,
    MNUITS      nvarchar(max) NULL,
    MDIMANCHES  nvarchar(max) NULL,
    MFERIES     nvarchar(max) NULL,
    TOTALH      nvarchar(max) NULL,
    CONSTRAINT PK_T_impr_HS PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   10. Frais / cantine
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_FRAIS', N'U') IS NULL
CREATE TABLE dbo.T_FRAIS (
    IDPOINTAGE        int       IDENTITY(1,1) NOT NULL,
    NOM_BDD_SAGE      nvarchar(128) NULL,
    NOM_BDD_POINTEUSE nvarchar(max) NULL,
    MATRICULE_SAGE    nvarchar(64)  NULL,
    DATEDEB           datetime2 NULL,
    DATEFIN           datetime2 NULL,
    TOTAL_POINT       int       NULL,
    CONSTRAINT PK_T_FRAIS PRIMARY KEY (IDPOINTAGE)
);
GO

IF OBJECT_ID(N'dbo.T_CANTINE', N'U') IS NULL
CREATE TABLE dbo.T_CANTINE (
    IDCANTINE         int       IDENTITY(1,1) NOT NULL,
    NOM_BDD_SAGE      nvarchar(128) NULL,
    NOM_BDD_POINTEUSE nvarchar(max) NULL,
    MATRICULE_SAGE    nvarchar(64)  NULL,
    DATEDEB           datetime2 NULL,
    DATEFIN           datetime2 NULL,
    TOTAL_CANTINE     int       NULL,
    CONSTRAINT PK_T_CANTINE PRIMARY KEY (IDCANTINE)
);
GO

/* =====================================================================================
   11. Traçabilité (audit) — tables « suivi_* »
   ===================================================================================== */
IF OBJECT_ID(N'dbo.suivi_SUIVI', N'U') IS NULL
CREATE TABLE dbo.suivi_SUIVI (
    Id         int           IDENTITY(1,1) NOT NULL,
    [_TABLES]   nvarchar(128) NULL,
    [_COLONNES] nvarchar(max) NULL,
    [_USERS]    nvarchar(max) NULL,
    [_ACTIONS]  nvarchar(128) NULL,
    [_DATETIMES] datetime2   NULL,
    [_NOUVEAUX] nvarchar(max) NULL,
    [_ANCIENS]  nvarchar(max) NULL,
    [_BASES]    nvarchar(max) NULL,
    CONSTRAINT PK_suivi_SUIVI PRIMARY KEY (Id)
);
GO

IF OBJECT_ID(N'dbo.suivi_T', N'U') IS NULL
CREATE TABLE dbo.suivi_T (
    Id         int           IDENTITY(1,1) NOT NULL,
    S_TABLES   nvarchar(max) NULL,
    S_COLONNES nvarchar(max) NULL,
    S_TYPES    nvarchar(max) NULL,
    BASES      nvarchar(max) NULL,
    CONSTRAINT PK_suivi_T PRIMARY KEY (Id)
);
GO

IF OBJECT_ID(N'dbo.suivi_TBLCLN', N'U') IS NULL
CREATE TABLE dbo.suivi_TBLCLN (
    Id       int           IDENTITY(1,1) NOT NULL,
    TABLES   nvarchar(max) NULL,
    COLONNES nvarchar(max) NULL,
    TYPES    nvarchar(max) NULL,
    ETATS    bit           NULL,
    BASES    nvarchar(max) NULL,
    CONSTRAINT PK_suivi_TBLCLN PRIMARY KEY (Id)
);
GO

/* =====================================================================================
   12. Mapping dynamique de sources (schéma créé par l'application au démarrage)
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_FIELD_ROLE', N'U') IS NULL
CREATE TABLE dbo.T_FIELD_ROLE (
    Code               nvarchar(64)  NOT NULL,
    Label              nvarchar(128) NULL,
    SystemType         nvarchar(16)  NOT NULL,
    EntityKind         nvarchar(32)  NOT NULL,
    IsRequired         bit           NOT NULL CONSTRAINT DF_T_FIELD_ROLE_IsRequired DEFAULT (0),
    AutoMappingPatterns nvarchar(256) NULL,
    SortOrder          int           NOT NULL CONSTRAINT DF_T_FIELD_ROLE_SortOrder DEFAULT (0),
    CONSTRAINT PK_T_FIELD_ROLE PRIMARY KEY (Code)
);
GO

IF OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING', N'U') IS NULL
CREATE TABLE dbo.T_SOURCE_ENTITY_MAPPING (
    Id             int           IDENTITY(1,1) NOT NULL,
    SystemType     nvarchar(16)  NOT NULL,
    SageDbId       int           NULL,
    PointeuseDbId  int           NULL,
    EntityKind     nvarchar(32)  NOT NULL,
    SourceTable    nvarchar(128) NULL,
    CONSTRAINT PK_T_SOURCE_ENTITY_MAPPING PRIMARY KEY (Id)
);
GO

IF OBJECT_ID(N'dbo.T_SOURCE_FIELD_MAPPING', N'U') IS NULL
CREATE TABLE dbo.T_SOURCE_FIELD_MAPPING (
    Id              int           IDENTITY(1,1) NOT NULL,
    EntityMappingId int           NOT NULL,
    FieldRoleCode   nvarchar(64)  NOT NULL,
    SourceColumn    nvarchar(128) NULL,
    CONSTRAINT PK_T_SOURCE_FIELD_MAPPING PRIMARY KEY (Id)
);
GO

/* =====================================================================================
   13. Code constante / écriture de paie dynamique
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_CODE_CONSTANTE', N'U') IS NULL
CREATE TABLE dbo.T_CODE_CONSTANTE (
    ID             int           IDENTITY(1,1) NOT NULL,
    CATEGORIE      nvarchar(max) NOT NULL,
    INTITULE       nvarchar(max) NULL,
    CODE_CONSTANTE nvarchar(max) NOT NULL,
    CONSTRAINT PK_T_CODE_CONSTANTE PRIMARY KEY (ID)
);
GO

IF OBJECT_ID(N'dbo.T_PAIE_ECRITURE', N'U') IS NULL
CREATE TABLE dbo.T_PAIE_ECRITURE (
    ID            int           IDENTITY(1,1) NOT NULL,
    BDD_SAGE      nvarchar(128) NOT NULL,
    TABLE_CIBLE   nvarchar(256) NOT NULL,
    COL_MATRICULE nvarchar(128) NOT NULL,
    CATEGORIE     nvarchar(32)  NOT NULL,
    COL_VALEUR    nvarchar(128) NOT NULL,
    CONSTRAINT PK_T_PAIE_ECRITURE PRIMARY KEY (ID)
);
GO

/* =====================================================================================
   13b. Archives des suppressions (ex-tables « Historique* ») — alimentées par l'application
   ===================================================================================== */
-- Chaque table est créée avec son index de pagination (HistoryArchiveConfiguration).
IF OBJECT_ID(N'dbo.T_HISTORIQUE_POINTAGE', N'U') IS NULL
BEGIN
CREATE TABLE dbo.T_HISTORIQUE_POINTAGE (
    ID                 int           IDENTITY(1,1) NOT NULL,
    POINTAGE_ID        decimal(18,0) NULL,
    NOM_BDD_SAGE       nvarchar(128) NULL,
    NOM_BDD_POINTEUSE  nvarchar(max) NULL,
    MATRICULE_SAGE     nvarchar(64)  NULL,
    DATE_POINTAGE      datetime2     NULL,
    HEURE_POINTAGE     time          NULL,
    DATE_IMPORTATION   datetime2     NULL,
    IDUNIQUE_POINTAGE  decimal(18,2) NULL,
    TYPE_POINTAGE      nvarchar(max) NULL,
    DATE_SUPPRESSION   datetime2     NOT NULL,
    CONSTRAINT PK_T_HISTORIQUE_POINTAGE PRIMARY KEY (ID)
);
CREATE INDEX IX_T_HISTORIQUE_POINTAGE_Pagination ON dbo.T_HISTORIQUE_POINTAGE (NOM_BDD_SAGE, DATE_SUPPRESSION);
END;
GO

IF OBJECT_ID(N'dbo.T_HISTORIQUE_ANOMALIE', N'U') IS NULL
BEGIN
CREATE TABLE dbo.T_HISTORIQUE_ANOMALIE (
    ID                 int           IDENTITY(1,1) NOT NULL,
    ANOMALIE_ID        decimal(18,0) NULL,
    BDD_SAGE           nvarchar(128) NULL,
    BDD_POINTEUSE      nvarchar(max) NULL,
    MATRICULE_SAGE     nvarchar(64)  NULL,
    DATE_POINTAGE_IN   datetime2     NULL,
    DATE_POINTAGE_OUT  datetime2     NULL,
    H1                 time          NULL,
    H2                 time          NULL,
    HENTREE            time          NULL,
    HPS                time          NULL,
    HPE                time          NULL,
    HSORTIE            time          NULL,
    ABS_AM             bit           NULL,
    ABS_PM             bit           NULL,
    FERIES             bit           NULL,
    FERIES_AM          bit           NULL,
    FERIES_PM          bit           NULL,
    INTITULE_ABSENCE   nvarchar(max) NULL,
    DATE_SUPPRESSION   datetime2     NOT NULL,
    CONSTRAINT PK_T_HISTORIQUE_ANOMALIE PRIMARY KEY (ID)
);
CREATE INDEX IX_T_HISTORIQUE_ANOMALIE_Pagination ON dbo.T_HISTORIQUE_ANOMALIE (BDD_SAGE, DATE_SUPPRESSION);
END;
GO

IF OBJECT_ID(N'dbo.T_HISTORIQUE_HS', N'U') IS NULL
BEGIN
CREATE TABLE dbo.T_HISTORIQUE_HS (
    ID                int           IDENTITY(1,1) NOT NULL,
    HS_ID             int           NULL,
    MATRICULE         nvarchar(64)  NULL,
    NUM_SAL           int           NULL,
    EXO130            decimal(18,2) NULL,
    EXO150            decimal(18,2) NULL,
    I130              decimal(18,2) NULL,
    I150              decimal(18,2) NULL,
    PERIODE_DEBUT     datetime2     NULL,
    PERIODE_FIN       datetime2     NULL,
    FERIE             decimal(18,2) NULL,
    NUIT              decimal(18,2) NULL,
    DIM               decimal(18,2) NULL,
    RETARD            decimal(18,2) NULL,
    ABSENCE           decimal(18,2) NULL,
    BDD_SAGE          nvarchar(128) NULL,
    BDD_POINTEUSE     nvarchar(max) NULL,
    DATE_SUPPRESSION  datetime2     NOT NULL,
    CONSTRAINT PK_T_HISTORIQUE_HS PRIMARY KEY (ID)
);
CREATE INDEX IX_T_HISTORIQUE_HS_Pagination ON dbo.T_HISTORIQUE_HS (BDD_SAGE, DATE_SUPPRESSION);
END;
GO

/* =====================================================================================
   13c. Droits par rôle et journal d'audit des droits
        (RolePermissionConfiguration.EnsureStorageAsync, create_role_permission_table.sql)
   ===================================================================================== */
IF OBJECT_ID(N'dbo.T_ROLE_PERMISSION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_ROLE_PERMISSION (
        ID              int           IDENTITY(1,1) NOT NULL,
        IDROLE          decimal(18,0) NOT NULL,
        PERMISSION_CODE nvarchar(128) NOT NULL,
        IS_GRANTED      bit           NOT NULL CONSTRAINT DF_T_ROLE_PERMISSION_IS_GRANTED DEFAULT (1),
        CONSTRAINT PK_T_ROLE_PERMISSION PRIMARY KEY (ID)
    );
    CREATE UNIQUE INDEX UX_T_ROLE_PERMISSION ON dbo.T_ROLE_PERMISSION (IDROLE, PERMISSION_CODE);
END;
GO

IF OBJECT_ID(N'dbo.T_AUDIT_DROIT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_AUDIT_DROIT (
        ID              int            IDENTITY(1,1) NOT NULL,
        DATE_ACTION     datetime2      NOT NULL CONSTRAINT DF_T_AUDIT_DROIT_DATE_ACTION DEFAULT SYSUTCDATETIME(),
        ID_OPERATEUR    decimal(18,0)  NOT NULL,
        LOGIN_OPERATEUR nvarchar(100)  NOT NULL,
        TYPE_CIBLE      nvarchar(20)   NOT NULL,
        ID_CIBLE        decimal(18,0)  NOT NULL,
        NOM_CIBLE       nvarchar(100)  NULL,
        ACTION          nvarchar(50)   NOT NULL,
        PERMS_AVANT     nvarchar(max)  NULL,
        PERMS_APRES     nvarchar(max)  NULL,
        CONSTRAINT PK_T_AUDIT_DROIT PRIMARY KEY (ID)
    );
    CREATE INDEX IX_T_AUDIT_DROIT_DATE  ON dbo.T_AUDIT_DROIT (DATE_ACTION DESC);
    CREATE INDEX IX_T_AUDIT_DROIT_CIBLE ON dbo.T_AUDIT_DROIT (TYPE_CIBLE, ID_CIBLE);
END;
GO

/* =====================================================================================
   13d. Colonnes ajoutées après coup sur les bases existantes (méthodes Ensure*)
   ===================================================================================== */
-- SageDatabaseConfiguration : mapping du code constante.
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_TABLE_CODE_CONSTANTE') IS NULL
    ALTER TABLE dbo.T_BDD_SAGE ADD MAP_TABLE_CODE_CONSTANTE nvarchar(128) NULL;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_CODE_CONSTANTE') IS NULL
    ALTER TABLE dbo.T_BDD_SAGE ADD MAP_COL_CODE_CONSTANTE nvarchar(128) NULL;
-- PunchProcessingConfiguration : origine des heures corrigées (AUTO | MANUEL).
IF COL_LENGTH(N'dbo.T_HEURECORRIGER', N'ORIGINE') IS NULL
    ALTER TABLE dbo.T_HEURECORRIGER ADD ORIGINE nvarchar(10) NULL;
-- UserAccountConfiguration : e-mail d'envoi des identifiants.
IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'EMAIL') IS NULL
    ALTER TABLE dbo.T_RESPONSABLE ADD EMAIL nvarchar(200) NULL;
GO

/* =====================================================================================
   14. Index et clé étrangère — BASE NEUVE UNIQUEMENT
       Sur une base existante, ces objets relèvent des scripts manuels (voir l'en-tête).
   ===================================================================================== */

-- Index uniques filtrés : la FK non applicable est NULL pour chaque ligne, or SQL Server
-- traite les NULL comme égaux dans un index unique. Sans le filtre, une seule ligne par
-- EntityKind pourrait exister pour tout le système (2e base Sage/Pointeuse refusée).
IF CAST(SESSION_CONTEXT(N'softtime_001_base_neuve') AS int) = 1
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
        CREATE UNIQUE INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (SageDbId, EntityKind) WHERE SageDbId IS NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
        CREATE UNIQUE INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (PointeuseDbId, EntityKind) WHERE PointeuseDbId IS NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SFM_Entity_Role' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_FIELD_MAPPING'))
        CREATE UNIQUE INDEX UX_SFM_Entity_Role ON dbo.T_SOURCE_FIELD_MAPPING (EntityMappingId, FieldRoleCode);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_PAIE_ECRITURE_BDD_CATEGORIE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_ECRITURE'))
        CREATE UNIQUE INDEX UX_T_PAIE_ECRITURE_BDD_CATEGORIE ON dbo.T_PAIE_ECRITURE (BDD_SAGE, CATEGORIE);

    -- Clé étrangère déclarée par EF (cascade).
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SFM_EntityMapping')
        ALTER TABLE dbo.T_SOURCE_FIELD_MAPPING
            ADD CONSTRAINT FK_SFM_EntityMapping FOREIGN KEY (EntityMappingId)
            REFERENCES dbo.T_SOURCE_ENTITY_MAPPING (Id) ON DELETE CASCADE;
END;
GO

/*  Index de pagination (add_pagination_performance_indexes.sql). Les colonnes de filtre sont
    bornées dans les CREATE TABLE ci-dessus, elles sont donc indexables. L'ordre des colonnes
    suit le filtre (base SAGE) puis l'ordre de pagination. Les index des tables d'archive
    (T_HISTORIQUE_*) sont créés avec leur table, section 13b. */
IF CAST(SESSION_CONTEXT(N'softtime_001_base_neuve') AS int) = 1
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_POINTAGE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_POINTAGE'))
        CREATE INDEX IX_T_POINTAGE_Pagination ON dbo.T_POINTAGE (NOM_BDD_SAGE, DATE_POINTAGE, HEURE_POINTAGE, MATRICULE_SAGE);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEURECORRIGER_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HEURECORRIGER'))
        CREATE INDEX IX_T_HEURECORRIGER_Pagination ON dbo.T_HEURECORRIGER (BDD_SAGE, DATE_POINTAGE_IN, MATRICULE_SAGE);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEUREANOMALIE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HEUREANOMALIE'))
        CREATE INDEX IX_T_HEUREANOMALIE_Pagination ON dbo.T_HEUREANOMALIE (BDD_SAGE, DATE_POINTAGE_IN, MATRICULE_SAGE);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_CANTINE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_CANTINE'))
        CREATE INDEX IX_T_CANTINE_Pagination ON dbo.T_CANTINE (NOM_BDD_SAGE, DATEDEB, DATEFIN, MATRICULE_SAGE);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_FRAIS_Pagination' AND object_id = OBJECT_ID(N'dbo.T_FRAIS'))
        CREATE INDEX IX_T_FRAIS_Pagination ON dbo.T_FRAIS (NOM_BDD_SAGE, DATEDEB, DATEFIN, MATRICULE_SAGE);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PLANNING_SAL_Pagination' AND object_id = OBJECT_ID(N'dbo.T_PLANNING_SAL'))
        CREATE INDEX IX_T_PLANNING_SAL_Pagination ON dbo.T_PLANNING_SAL (BDD_SAGE, DateP, IDSAL);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_CARDPAIE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_CARDPAIE'))
        CREATE INDEX IX_T_CARDPAIE_Pagination ON dbo.T_CARDPAIE (SAGE_BDD, SAGE_MATRICULE, ID);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HS_VALIDER_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HS_VALIDER'))
        CREATE INDEX IX_T_HS_VALIDER_Pagination ON dbo.T_HS_VALIDER (BDD_SAGE, DATE_DEB, DATE_FIN, MATRICULES);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HSExoImp_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HSExoImp'))
        CREATE INDEX IX_T_HSExoImp_Pagination ON dbo.T_HSExoImp (BDD_SAGE, PeriodeDebut, PeriodeFin, Matricule);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HSSemaine_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HSSemaine'))
        CREATE INDEX IX_T_HSSemaine_Pagination ON dbo.T_HSSemaine (BDD_SAGE, PeriodeDebut, PeriodeFin, Matricule);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEUREDIMANCHE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HEUREDIMANCHE'))
        CREATE INDEX IX_T_HEUREDIMANCHE_Pagination ON dbo.T_HEUREDIMANCHE (BDD_SAGE, Date_Dim, MATRICULE_SAGE);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_SUIVI_Date' AND object_id = OBJECT_ID(N'dbo.suivi_SUIVI'))
        CREATE INDEX IX_suivi_SUIVI_Date ON dbo.suivi_SUIVI ([_DATETIMES]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_SUIVI_Filtres' AND object_id = OBJECT_ID(N'dbo.suivi_SUIVI'))
        CREATE INDEX IX_suivi_SUIVI_Filtres ON dbo.suivi_SUIVI ([_TABLES], [_ACTIONS], [_DATETIMES]);
END;
GO

/* =====================================================================================
   15. DONNÉES DE DÉMARRAGE — insérées seulement dans une table VIDE.
       Aucun compte utilisateur (T_RESPONSABLE) n'est créé.
   ===================================================================================== */

-- 15.1 Droits applicatifs (Rights.cs : 1..6). IDENTITY_INSERT pour garantir les identifiants.
IF NOT EXISTS (SELECT 1 FROM dbo.T_DROIT)
BEGIN
    SET IDENTITY_INSERT dbo.T_DROIT ON;
    INSERT INTO dbo.T_DROIT (IDDROIT, DROIT) VALUES
        (1, N'Bases de données'),
        (2, N'Paramètres'),
        (3, N'Traitement'),
        (4, N'Autres'),
        (5, N'Traçabilité'),
        (6, N'Rapports');
    SET IDENTITY_INSERT dbo.T_DROIT OFF;
END;
GO

-- 15.2 Rôle d'identifiant 1 « Administrateur » (AdminRules.AdminRoleId), puis tous les droits.
IF NOT EXISTS (SELECT 1 FROM dbo.T_ROLE)
BEGIN
    SET IDENTITY_INSERT dbo.T_ROLE ON;
    INSERT INTO dbo.T_ROLE (IDROLE, NOMROLE) VALUES (1, N'Administrateur');
    SET IDENTITY_INSERT dbo.T_ROLE OFF;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.T_DROIT_ROLE)
   AND EXISTS (SELECT 1 FROM dbo.T_ROLE WHERE IDROLE = 1)
    INSERT INTO dbo.T_DROIT_ROLE (IDDROIT, IDROLE)
    SELECT d.IDDROIT, 1 FROM dbo.T_DROIT d;
GO

-- 15.3 Catalogue des rôles de champs (T_FIELD_ROLE) — miroir de CatalogService.EnsureMappingSchemaAsync.
IF NOT EXISTS (SELECT 1 FROM dbo.T_FIELD_ROLE)
INSERT INTO dbo.T_FIELD_ROLE (Code, Label, SystemType, EntityKind, IsRequired, AutoMappingPatterns, SortOrder)
SELECT v.Code, v.Label, v.SystemType, v.EntityKind, v.IsRequired, v.AutoMappingPatterns, v.SortOrder
FROM (VALUES
    (N'SAGE_MATRICULE', N'Matricule', N'SAGE', N'EMPLOYEE', 1, N'matricule|matric|mat', 10),
    (N'SAGE_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'EMPLOYEE', 1, N'compteur|numsalarie|num_salarie|salarieid', 20),
    (N'SAGE_NOM', N'Nom', N'SAGE', N'EMPLOYEE', 0, N'nom|name', 30),
    (N'SAGE_PRENOM', N'Prénom', N'SAGE', N'EMPLOYEE', 0, N'prenom|prénom|firstname', 40),
    (N'SAGE_BADGE', N'Numéro de badge', N'SAGE', N'EMPLOYEE', 0, N'badge|badgenumber|numbadge|num_badge|numerobadge', 50),
    (N'SAGE_INACTIVE_FLAG', N'Indicateur désactivé', N'SAGE', N'EMPLOYEE', 0, N'desactive|désactivé|inactive|actif', 60),
    (N'SAGE_AFF_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'AFFECTATION', 1, N'numsalarie|num_salarie|compteur|salarieid', 10),
    (N'SAGE_AFF_DEPARTEMENT', N'Code département', N'SAGE', N'AFFECTATION', 0, N'departement|département|code_dep|dep', 20),
    (N'SAGE_AFF_SERVICE', N'Service', N'SAGE', N'AFFECTATION', 0, N'service', 30),
    (N'SAGE_AFF_START', N'Date début', N'SAGE', N'AFFECTATION', 0, N'datedebut|date_debut|debut|startdate', 40),
    (N'SAGE_AFF_END', N'Date sortie', N'SAGE', N'AFFECTATION', 0, N'datesortie|date_sortie|datefin|fin|enddate', 50),
    (N'SAGE_DEPT_CODE', N'Code département', N'SAGE', N'DEPARTMENT', 1, N'code|code_dep|code_departement', 10),
    (N'SAGE_DEPT_LABEL', N'Intitulé département', N'SAGE', N'DEPARTMENT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
    (N'SAGE_CAL_DATE', N'Date', N'SAGE', N'COMPANY_CALENDAR', 1, N'periode|date|jour', 10),
    (N'SAGE_CAL_IS_HOLIDAY', N'Jour férié', N'SAGE', N'COMPANY_CALENDAR', 0, N'etat|etatjour|ferie|férié|holiday', 20),
    (N'SAGE_CST_CODE', N'Code constante', N'SAGE', N'CONSTANT', 1, N'code|codeconstante|code_constante', 10),
    (N'SAGE_CST_LABEL', N'Intitulé', N'SAGE', N'CONSTANT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
    (N'SAGE_CST_OPERANDE', N'Opérande', N'SAGE', N'CONSTANT', 0, N'operande|codeoperande|ordre|noordre', 30),
    (N'SAGE_EVT_CODE', N'Code événement', N'SAGE', N'ABSENCE_EVENT', 0, N'code|codene|code_ne', 10),
    (N'SAGE_EVT_LABEL', N'Intitulé', N'SAGE', N'ABSENCE_EVENT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
    (N'SAGE_EEV_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'EMPLOYEE_EVENT', 1, N'numsalarie|num_salarie|compteur|salarieid', 10),
    (N'SAGE_EEV_CODE', N'Code événement', N'SAGE', N'EMPLOYEE_EVENT', 0, N'code|codene|code_ne', 20),
    (N'SAGE_EEV_START', N'Début période', N'SAGE', N'EMPLOYEE_EVENT', 0, N'periode|debut|start|datedebut', 30),
    (N'SAGE_EEV_END', N'Fin période', N'SAGE', N'EMPLOYEE_EVENT', 0, N'periode|fin|end|datefin', 40),
    (N'SAGE_EEV_MATIN', N'Matin', N'SAGE', N'EMPLOYEE_EVENT', 0, N'matin|am', 50),
    (N'SAGE_EEV_APRESMIDI', N'Après-midi', N'SAGE', N'EMPLOYEE_EVENT', 0, N'apresmidi|après-midi|pm', 60),
    (N'PTE_USER_ID', N'Identifiant utilisateur', N'POINTEUSE', N'PUNCH_USER', 1, N'userid|user_id|id', 10),
    (N'PTE_USER_BADGE', N'Badge', N'POINTEUSE', N'PUNCH_USER', 0, N'badge|badgenumber|badge_number', 20),
    (N'PTE_USER_SSN', N'SSN / matricule', N'POINTEUSE', N'PUNCH_USER', 0, N'ssn|matricule|matric', 30),
    (N'PTE_USER_NAME', N'Nom', N'POINTEUSE', N'PUNCH_USER', 0, N'name|nom', 40),
    (N'PTE_PUNCH_USER_ID', N'Identifiant utilisateur', N'POINTEUSE', N'PUNCH', 1, N'userid|user_id|id', 10),
    (N'PTE_PUNCH_DATETIME', N'Date/heure', N'POINTEUSE', N'PUNCH', 1, N'checktime|check_time|datetime|date', 20),
    (N'PTE_PUNCH_TYPE', N'Type (entrée/sortie)', N'POINTEUSE', N'PUNCH', 0, N'checktype|check_type|type|sens', 30)
) AS v(Code, Label, SystemType, EntityKind, IsRequired, AutoMappingPatterns, SortOrder);
GO

-- 15.4 Codes constantes de paie (T_CODE_CONSTANTE) — miroir de OvertimeCodeKeys.Defaults.
IF NOT EXISTS (SELECT 1 FROM dbo.T_CODE_CONSTANTE)
INSERT INTO dbo.T_CODE_CONSTANTE (CATEGORIE, INTITULE, CODE_CONSTANTE)
SELECT v.CATEGORIE, v.INTITULE, v.CODE_CONSTANTE
FROM (VALUES
    (N'Exo130', N'EXO 130%', N'HS01'),
    (N'Exo150', N'EXO 150%', N'HS02'),
    (N'I130',   N'IMPO 130%', N'HS03'),
    (N'I150',   N'IMPO 150%', N'HS04'),
    (N'Ferie',  N'Férié',     N'HS05'),
    (N'Dim',    N'Dimanche',  N'HS06'),
    (N'Nuit',   N'Nuit',      N'HS07')
) AS v(CATEGORIE, INTITULE, CODE_CONSTANTE);
GO

-- 15.5 Paramètre horloge (singleton attendu par CatalogService.GetClockAsync).
IF NOT EXISTS (SELECT 1 FROM dbo.T_CLOCK)
    INSERT INTO dbo.T_CLOCK (MULTIPOINT) VALUES (0);
GO
