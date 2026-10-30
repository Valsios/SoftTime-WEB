# Migrations base de données

Deux familles coexistent :

- les scripts SQL historiques (`*.sql`) de la base fonctionnelle `dbo` ;
- les migrations **EF Core** du moteur prédictif, versionnées dans
  `SoftTime.Infrastructure/Migrations/Ml` (contexte `MlDbContext`, schéma `ml`).

## Règles

1. Les migrations sont versionnées dans Git, jamais appliquées à la main sur une base de production.
2. Une migration déjà appliquée en production n'est **jamais** modifiée : on ajoute une nouvelle migration.
3. Le nom doit être descriptif (`AddMlPredictionSchema`, `AddUserLockedUntilColumn`, …).
4. `Up` **et** `Down` sont testés sur une base jetable avant tout push.
5. Le déploiement utilise un script idempotent généré par EF.

## Commandes (depuis `softtime_backend/`)

```powershell
# Nouvelle migration
dotnet ef migrations add <NomDescriptif> `
  --project SoftTime.Infrastructure `
  --startup-project SoftTime.Api `
  --context MlDbContext `
  --output-dir Migrations/Ml

# Script idempotent de déploiement (ajoute l'en-tête QUOTED_IDENTIFIER pour sqlcmd)
dotnet ef migrations script --idempotent `
  --project SoftTime.Infrastructure `
  --startup-project SoftTime.Api `
  --context MlDbContext `
  -o migrations/<NomDescriptif>.sql

# Test Up sur une base jetable
dotnet ef database update `
  --project SoftTime.Infrastructure `
  --startup-project SoftTime.Api `
  --context MlDbContext `
  --connection "Data Source=localhost\SQLEXPRESS;Initial Catalog=SoftTime_Ml_Test;User ID=sa;Password=***;TrustServerCertificate=True"

# Test Down (retour à zéro)
dotnet ef database update 0 `
  --project SoftTime.Infrastructure `
  --startup-project SoftTime.Api `
  --context MlDbContext `
  --connection "<même chaîne>"
```

Le script généré doit être exécuté avec `sqlcmd -I` (ou contenir `SET QUOTED_IDENTIFIER ON`,
ce que fait l'en-tête ajouté) à cause de l'index filtré `IX_Modele_BddSage_TypeModele_EstActif`.

## Application au démarrage

`MlStorageConfiguration.EnsureSchemaAsync` applique les migrations du contexte `MlDbContext`
au démarrage de l'API (schéma `ml` dans la base `SoftTime`, ou base dédiée si
`ConnectionStrings:SoftTimeMl` est renseignée). Un échec est journalisé et n'empêche pas
l'API de démarrer.
