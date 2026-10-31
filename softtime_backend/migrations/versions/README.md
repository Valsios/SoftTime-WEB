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

## Ajouter un script

1. Prendre le **numéro suivant** : `002_ma_modification.sql` (jamais réutiliser ni renuméroter).
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
| `cleanup_duplicate_cards.sql` | Nettoyage des cartes en double (destructif) |
| `cleanup_orphan_source_mappings.sql` | Nettoyage des mappings orphelins (destructif) |
| `drop_legacy_cardpaie_branche.sql` | Suppression de la colonne héritée T_CARDPAIE.BRANCHE (destructif) |
| `drop_legacy_cardpaie_sauve.sql` | Suppression de T_CARDPAIE_SAUVE (destructif) |
| `drop_legacy_mapping.sql` | Suppression de l'ancien mapping (destructif) |
| `fix_planning_sal_night.sql` | Réparation des plannings salariés existants (données) |
| `benchmark_pagination.sql` | Mesure de performance, aucune modification |
