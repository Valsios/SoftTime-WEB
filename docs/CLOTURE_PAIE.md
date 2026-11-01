# Clôture de paie assistée — où modifier quoi

Route de lecture : `GET api/closing/analysis?from=&to=&departement=&includeComparison=&page=&pageSize=`
(en-tête `X-Sage-Database` requis). Accès : administrateur, ou porte 6 + permission `REPORTS:CLOSING`
+ lecture des bases SAGE et pointeuse actives. Sans dates : mois civil complet précédent.

## Réglages (section `Closing`, facultative)

Toutes les valeurs par défaut sont dans le code (`SoftTime.Application/Services/ClosingOptions.cs`).
La section n'a pas à exister ; une clé absente garde sa valeur par défaut. Un réglage invalide
**refuse le démarrage** avec un message « Réglage de clôture invalide : … ».

| Clé | Défaut | Règle |
|---|---|---|
| `Closing:Enabled` | `true` | `false` : la route répond 404 « désactivée » |
| `Closing:ShowNames` | `true` | `false` : matricule seul (nom et prénom `null`) |
| `Closing:DuplicateToleranceSeconds` | `120` | ≥ 0 ; quasi-doublon si écart strictement inférieur |
| `Closing:IgnorerSemainesValidees` | `true` | pointages impairs : ignorer les semaines validées |
| `Closing:MaxPeriodDays` | `366` | 1 à 366 |
| `Closing:CriticalRatioBlocking` | `0.02` | 0 à 1, point décimal ; **valeur provisoire à confirmer par le manager** |
| `Closing:Gravites:<Type>` | (gravité par défaut) | `Mineure`, `Importante` ou `Critique` |
| `Closing:Poids:<Gravité>` | `Mineure` 1, `Importante` 3, `Critique` 5 | ≥ 0, au moins un poids > 0 |

Exemple (`appsettings.json` ou variables d'environnement `Closing__Poids__Critique=10`) :

```json
"Closing": {
  "Enabled": true,
  "ShowNames": true,
  "DuplicateToleranceSeconds": 120,
  "IgnorerSemainesValidees": true,
  "MaxPeriodDays": 366,
  "CriticalRatioBlocking": 0.02,
  "Gravites": { "QuasiDoublon": "Importante" },
  "Poids": { "Mineure": 1, "Importante": 3, "Critique": 5 }
}
```

Types d'anomalie (`<Type>`) : `PointageIncomplet`, `PointageManquant`, `PlanningSansPointage`,
`PointageSansPlanning`, `DoublonExact`, `QuasiDoublon`, `FerieNonApplique`.

## Tâches courantes

- **Changer un seuil** (tolérance, ratio bloquant, période maximale) : la clé correspondante de la
  section `Closing`. Pour changer la valeur par défaut elle-même : `ClosingOptions.cs`
  (et `ClosingStatusRules.DefaultCriticalRatioBlocking` pour le ratio).
- **Changer la gravité d'un type** : `Closing:Gravites:<Type>`. Défaut en code :
  `PayrollClosingRules.GraviteParDefaut` (`SoftTime.Domain/Services/PayrollClosingRules.cs`).
- **Changer le poids du score** : `Closing:Poids:<Gravité>`. Formule (texte réutilisable par l'écran) :
  `PayrollClosingRules.FormuleScore` ; calcul : `PayrollClosingRules.Score`.
- **Changer les statuts** : `ClosingStatusRules.Statut` et `Libelle`
  (`SoftTime.Domain/Services/PayrollClosingStatus.cs`). Prêt = 0 critique ; Bloquant = critiques ÷
  jours-employé contrôlés ≥ `CriticalRatioBlocking` ; sinon À corriger ; aucun statut sans jour contrôlé.
- **Désactiver la fonction** : `Closing:Enabled = false` (route 404, aucune erreur 500), ou retirer
  `REPORTS:CLOSING` aux comptes concernés (page Privilèges, catégorie États).
- **Ajouter un type d'anomalie** (4 endroits) :
  1. l'énumération `ClosingAnomalyType` (`SoftTime.Domain/Models/PayrollClosing.cs`) ;
  2. la règle dans `PayrollClosingRules` (liste en entrée, `ClosingAnomaly` triées en sortie), appelée
     depuis `ClosingService.Controler` ;
  3. sa gravité par défaut dans `PayrollClosingRules.GraviteParDefaut` ;
  4. ses tests (un positif, un négatif) dans `SoftTime.Tests/PayrollClosingRulesTests.cs`.

## Règles à ne pas casser

- Le Domain ne lit jamais la configuration : `ClosingService` lui passe `ClosingRulesOptions`.
- Une ligne de planning sans heures (HA = HD, shift Pause) ne génère jamais d'anomalie.
- Aucune règle existante (absences, retards, HS, pré-traitement) n'est modifiée : elles sont réutilisées.
- Lecture seule : la route n'écrit rien (POST, PUT, PATCH, DELETE répondent 405).
