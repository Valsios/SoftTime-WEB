# Migrations base de données

Le schéma de la base fonctionnelle `dbo` est géré par des scripts SQL :

- les scripts **versionnés** de `versions/`, appliqués par `SchemaMigrator` (voir `versions/README.md`) ;
- les scripts SQL historiques (`*.sql`) de ce dossier, appliqués à la main quand ils sont nécessaires.

## Règles

1. Les migrations sont versionnées dans Git, jamais appliquées à la main sur une base de production.
2. Une migration déjà appliquée en production n'est **jamais** modifiée : on ajoute une nouvelle migration.
3. Le nom doit être descriptif (`AddUserLockedUntilColumn`, …).
4. Chaque script est testé sur une base jetable avant tout push, et reste idempotent.

## Ancien schéma `ml`

Le moteur prédictif (ML.NET) a été retiré : les prévisions d'heures supplémentaires sont maintenant
un atterrissage calculé à la demande, sans stockage. L'application ne lit plus et n'écrit plus le
schéma `ml`, et ne le crée plus au démarrage.

Sur une base où il existe déjà, il peut être supprimé avec `DropMlPredictionSchema.sql` (idempotent).
Ce script **efface définitivement** l'historique analytique, les modèles et les prévisions stockés :
il n'est jamais lancé automatiquement, à exécuter à la main après sauvegarde.
