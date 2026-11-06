# Migrations SQL versionnées

## Principe

Les scripts de ce dossier décrivent l'évolution du schéma de la base SoftTime (schéma `dbo`).
Ils sont **embarqués comme ressources** dans `SoftTime.Infrastructure` et appliqués par
`SchemaMigrator` (`SoftTime.Infrastructure/SchemaMigrations`) :

- les fichiers sont nommés `NNN_description.sql` (trois chiffres, numéros uniques) et exécutés
  dans l'ordre numérique ;
- la table `dbo.T_SCHEMA_VERSION` (créée par l'exécuteur) garde, pour chaque script appliqué :
  version, nom, somme de contrôle SHA-256, date (UTC) et machine\utilisateur ;
- un script est découpé sur les lignes `GO` et exécuté **dans une transaction** : en cas d'erreur,
  la transaction est annulée, l'erreur est journalisée et le démarrage s'arrête avec un message clair ;
- un verrou `sp_getapplock` (ressource `SoftTime.SchemaMigration`) empêche deux démarrages
  simultanés d'appliquer les mêmes scripts ;
- un script déjà appliqué n'est **jamais rejoué** ; si son contenu a changé (somme de contrôle
  différente), un avertissement est journalisé. Les fins de ligne sont normalisées avant le calcul
  (CRLF et LF donnent la même somme).

Le schéma `ml` (moteur prédictif) reste géré par les migrations EF Core (`EfMlStore.MigrateAsync`)
et n'est pas concerné.

## Pour les développeurs après un `git pull`

1. Lancer l'API comme d'habitude (`dotnet run` dans `SoftTime.Api`, ou depuis Visual Studio) : le profil `http`
   utilise `ASPNETCORE_ENVIRONMENT=Development`, donc `appsettings.Development.json` s'applique et
   `Database:AutoMigrate` vaut `true`. La chaîne de connexion et la clé JWT ne sont **plus** dans `appsettings.json` :
   définissez-les une fois par poste dans les secrets utilisateur (dans `SoftTime.Api`) :
   `dotnet user-secrets set "ConnectionStrings:SoftTime" "<chaîne>"` et
   `dotnet user-secrets set "Jwt:Key" "<au moins 32 caractères aléatoires>"`. Sans eux, l'API refuse de démarrer.
2. Regarder la ligne du journal au démarrage :
   - `Migrations de schéma : N script(s) appliqué(s), M déjà à jour.` : tout est en ordre ;
   - `Migrations de schéma désactivées (Database:AutoMigrate = false).` : l'activation est coupée (autre
     environnement, ou valeur remplacée) ; sans schéma, l'API refuse alors de démarrer avec un message clair.
3. Aucun SQL à exécuter à la main : les nouveaux scripts de `versions/` s'appliquent seuls, dans l'ordre, une seule fois.

**Règle d'or : faites une sauvegarde avant d'activer l'application automatique sur une base qui contient des
données utiles** (`BACKUP DATABASE <base> TO DISK = '<fichier>' WITH COPY_ONLY`). Les scripts ne sont jamais
destructifs, mais seule la sauvegarde permet un retour arrière fiable.

## Première installation (base vide)

1. Créer une base vide (`CREATE DATABASE <nom>`) et la renseigner dans la chaîne de connexion locale
   (`ConnectionStrings:SoftTime` en secret utilisateur ou variable d'environnement `ConnectionStrings__SoftTime`,
   jamais dans le dépôt), avec la clé `Jwt:Key`.
2. Lancer l'API avec `Database:AutoMigrate = true` : elle crée tout le schéma (001 à 005), le schéma `ml` du moteur
   prédictif, le rôle 1 « Administrateur », les six droits et les données de départ. Aucun compte n'est créé.
3. Créer le premier administrateur : `GET /api/auth/is-first-run` répond `true`, puis
   `POST /api/auth/create-admin` (nom, login, mot de passe conforme à la politique). Ensuite `is-first-run` répond `false`.
4. Se connecter normalement. Les bases SAGE et pointeuse se déclarent ensuite dans l'application.

## Que faire si une migration échoue

- Le démarrage s'arrête avec un message en français, sans chaîne de connexion ; chaque script s'exécute dans
  une transaction annulée en cas d'erreur : la base reste dans l'état précédent.
- « le verrou … n'a pas été obtenu » : une autre instance applique les migrations. Attendre qu'elle ait démarré,
  puis relancer ; au besoin augmenter `Database:AutoMigrateLockTimeoutSeconds`.
- « connexion à la base SoftTime refusée » : vérifier le serveur, la base et les droits du compte Windows
  (il faut pouvoir créer des objets sur la base : rôle `db_owner` en développement).
- « Le schéma de la base est absent » : activer `Database:AutoMigrate` (développement) ou exécuter
  `001_schema_initial.sql` à la main.
- Erreur dans un script : restaurer la sauvegarde si la base doit revenir en arrière, désactiver
  `Database:AutoMigrate`, corriger le script dans un **nouveau** numéro (ne jamais modifier un script déjà appliqué)
  ou `git revert` du commit fautif, puis relancer.

## Activer

Dans la configuration de l'API (fichier local, variables d'environnement…) :

```json
"Database": {
  "AutoMigrate": true,
  "AutoMigrateLockTimeoutSeconds": 60
}
```

- `Database:AutoMigrate` : faux par défaut. Faux, rien ne change au démarrage.
  Vrai, l'exécuteur tourne **avant** les méthodes `Ensure*` de `Program.cs`.
- `Database:AutoMigrateLockTimeoutSeconds` : attente maximale du verrou (60 s par défaut).

Usage recommandé : activé en développement et pour installer une base neuve. En production,
préférer l'exécution manuelle après sauvegarde :

```
sqlcmd -S <instance> -E -C -d <base> -i 001_schema_initial.sql
```

Sans `Database:AutoMigrate` et sans schéma, l'API refuse de démarrer avec le message
« Le schéma de la base est absent : … ».

## 001_schema_initial.sql

Schéma complet de l'application, rejouable : tables de `create_softtime_database.sql`,
`T_ROLE_PERMISSION` et `T_AUDIT_DROIT`, colonnes ajoutées par les `Ensure*`, index de pagination,
données de départ (rôle 1 « Administrateur », 6 droits, lien du rôle 1, rôles de champs, codes
constantes, `T_CLOCK`). **Aucun compte utilisateur** : le premier compte se crée par
`GET /api/auth/is-first-run` puis `POST /api/auth/create-admin`.

Sur une base **existante** (T_ROLE déjà présente), 001 ajoute seulement les tables manquantes avec
leurs index, les colonnes des `Ensure*` et les données de départ dans les tables **vides** ; il ne
construit aucun index et aucune clé étrangère sur une table existante.

## 002_integrite_base_neuve.sql

Ajoute à une base neuve les index **uniques** présents dans les anciennes bases mais absents du schéma initial.
Aujourd'hui : `UX_T_ACTIVECORRESP_PAIRE` (paires SAGE/pointeuse actives, filtré). Rejouable ; si les colonnes ne
sont pas indexables (`nvarchar(max)` dans une base neuve) ou s'il existe des doublons, l'index est **ignoré** avec
un `PRINT`. Sur une ancienne base où l'index existe déjà, 002 ne change rien. Le bornage des colonnes
(`ALTER COLUMN`) reste dans le script manuel `add_pagination_performance_indexes.sql`.

## 003_journal_central.sql

Crée `dbo.suivi_JOURNAL`, le **journal central des modifications** alimenté par l'intercepteur de
`SoftTimeDbContext` (entité `suivi_JOURNAL`, 13 colonnes) et ses index non uniques : date, base, table/action/date,
corrélation, utilisateur (`IX_suivi_JOURNAL_User_Date`) et objet (`IX_suivi_JOURNAL_Cles_Date`). Rejouable : sur une
table existante, aucune colonne n'est modifiée et seuls les index manquants sont ajoutés ; un index dont les
colonnes ne sont pas indexables est **ignoré** avec un `PRINT`. Ne touche ni `suivi_SUIVI`, ni `suivi_TBLCLN`, ni les
déclencheurs, ni `T_AUDIT_DROIT`.

Cette migration remplace l'ancienne création au démarrage (`AuditJournalConfiguration`, supprimée) : le script manuel
`create_audit_journal.sql` **n'est plus nécessaire**. Sans la table et sans `Database:AutoMigrate`, l'API refuse de
démarrer avec le message « Le journal d'audit est absent : activez Database:AutoMigrate (développement) ou exécutez
migrations/versions/003_journal_central.sql. » (au lieu d'une erreur SQL à chaque écriture ; désactivable par
`AuditJournal:Enabled = false`).

## 005_unicite_login.sql

Empêche deux comptes de partager un login, même s'ils ne diffèrent que par la casse ou par des espaces.

- Ajoute à `T_RESPONSABLE` la colonne calculée persistée `LOGIN_CLE` : `UPPER(LTRIM(RTRIM(LOGIN)))`, bornée à
  256 caractères. C'est la même clé que `LoginKey` côté application. Un login vide ou NULL reçoit une clé propre à son
  compte, il ne bloque donc pas les autres.
- Pose l'index unique `UX_T_RESPONSABLE_LOGIN_CLE` sur cette colonne.
- **Si la base contient déjà des doublons**, l'index n'est pas créé et le démarrage continue. Le journal affiche
  la liste des logins en double. Renommez ces comptes dans l'écran Utilisateurs, puis exécutez à la main :
  `CREATE UNIQUE INDEX UX_T_RESPONSABLE_LOGIN_CLE ON dbo.T_RESPONSABLE (LOGIN_CLE);`
  En attendant, l'application refuse la connexion à un login ambigu, sauf si l'un des comptes porte exactement
  le login saisi.
- **Applications qui écrivent aussi dans `T_RESPONSABLE`** (ancienne application de bureau, scripts) : un index sur
  une colonne calculée exige que leurs sessions aient `ANSI_NULLS`, `ANSI_PADDING`, `ANSI_WARNINGS`, `ARITHABORT`,
  `CONCAT_NULL_YIELDS_NULL` et `QUOTED_IDENTIFIER` à `ON`. C'est le cas par défaut avec SqlClient et ODBC récents (`ARITHABORT` est alors implicite, car
  `ANSI_WARNINGS` vaut `ON`).
  Sinon, leurs écritures dans cette table échouent avec l'erreur 1934.

## Ajouter un script

1. Prendre le **numéro suivant** : `006_ma_modification.sql` (jamais réutiliser ni renuméroter).
2. Le rendre **rejouable** : chaque objet créé seulement s'il n'existe pas
   (`IF OBJECT_ID(...) IS NULL`, `IF COL_LENGTH(...) IS NULL`, `IF NOT EXISTS (SELECT 1 FROM sys.indexes ...)`).
3. **Jamais destructif** : pas de `DROP`, `DELETE`, `TRUNCATE`, ni `ALTER COLUMN` qui réduit ou
   supprime. Ces opérations restent des scripts manuels, exécutés après sauvegarde.
4. **Jamais de secret** : ni mot de passe, ni chaîne de connexion, ni compte utilisateur.
5. Pas de `USE` ni de `CREATE DATABASE` : le script s'applique à la base courante.
6. Ne jamais modifier un script déjà appliqué : corriger par un nouveau script.
7. Le fichier est embarqué automatiquement (`..\migrations\versions\*.sql` dans
   `SoftTime.Infrastructure.csproj`) ; ajouter un test si le script porte une règle métier.

## Revenir en arrière

Les scripts ne contiennent pas de retour arrière automatique :

1. **Avant** toute migration en production : sauvegarde complète
   (`BACKUP DATABASE ... WITH COPY_ONLY`) ; c'est le seul retour arrière fiable.
2. Désactiver `Database:AutoMigrate`, restaurer la sauvegarde si le schéma doit revenir à l'état antérieur.
3. Côté code : `git revert` du commit qui a ajouté le script, puis redéployer.
4. Supprimer la ligne correspondante de `T_SCHEMA_VERSION` uniquement si le schéma a été restauré
   à l'état antérieur (opération manuelle de DBA).

## Scripts manuels (JAMAIS exécutés automatiquement)

Ces scripts du dossier `migrations/` restent à exécuter à la main, après sauvegarde :

| Script | Rôle |
| --- | --- |
| `create_softtime_database.sql` | Ancien script de création complète (crée la base et un compte de démarrage) |
| `add_pagination_performance_indexes.sql` | Borne les colonnes de filtre (ALTER COLUMN) et crée les index de pagination sur une base existante |
| `add_performance_indexes.sql` | Index de dates sur une base existante |
| `AddMlPredictionSchema.sql` | Équivalent SQL de la migration EF du schéma `ml` |
| `audit_and_migrate_passwords.sql` | Audit et migration des mots de passe |
| `upgrade_password_column_schema.sql` | Élargissement de la colonne PASSWORD |
| `create_role_permission_table.sql` | Tables des droits par rôle (repris dans 001) |
| `create_audit_journal.sql` | Journal central `suivi_JOURNAL` (repris dans 003, n'est plus nécessaire) |
| `cleanup_duplicate_cards.sql` | Nettoyage des cartes en double (destructif) |
| `cleanup_orphan_source_mappings.sql` | Nettoyage des mappings orphelins (destructif) |
| `drop_legacy_cardpaie_branche.sql` | Suppression de la colonne héritée T_CARDPAIE.BRANCHE (destructif) |
| `drop_legacy_cardpaie_sauve.sql` | Suppression de T_CARDPAIE_SAUVE (destructif) |
| `drop_legacy_mapping.sql` | Suppression de l'ancien mapping (destructif) |
| `fix_planning_sal_night.sql` | Réparation des plannings salariés existants (données) |
| `benchmark_pagination.sql` | Mesure de performance, aucune modification |
