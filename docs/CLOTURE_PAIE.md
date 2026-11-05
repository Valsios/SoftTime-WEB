# Contrôle avant paie assisté — où modifier quoi

*Anciennement : Clôture de paie.*

Route de lecture : `GET api/closing/analysis?from=&to=&departement=&includeComparison=&includeInformation=&page=&pageSize=`
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
| `Closing:DuplicateToleranceSeconds` | `30` | ≥ 0 ; quasi-doublon si écart strictement inférieur (29 s oui, 30 s et 31 s non) |
| `Closing:IgnorerSemainesValidees` | `true` | pointages impairs : ignorer les semaines validées |
| `Closing:ExclureSansPlanningSurPeriode` | `true` | « pointage sans planning » ignore les salariés sans AUCUNE ligne de planning (travail, OFF ou Pause) sur la période |
| `Closing:DoublonsMonoGravite` | `Mineure` | gravité des doublons en mono-pointage (`Information`, `Mineure`, `Importante` ou `Critique`) |
| `Closing:MaxPeriodDays` | `366` | 1 à 366 |
| `Closing:CriticalRatioBlocking` | `0.05` | 0 à 1, point décimal ; **valeur provisoire à confirmer par le manager** |
| `Closing:Gravites:<Type>` | (gravité par défaut) | `Information`, `Mineure`, `Importante` ou `Critique` |
| `Closing:Poids:<Gravité>` | `Information` 0, `Mineure` 1, `Importante` 3, `Critique` 5 | ≥ 0, au moins un poids > 0 |

Exemple (`appsettings.json` ou variables d'environnement `Closing__Poids__Critique=10`) :

```json
"Closing": {
  "Enabled": true,
  "ShowNames": true,
  "DuplicateToleranceSeconds": 30,
  "IgnorerSemainesValidees": true,
  "ExclureSansPlanningSurPeriode": true,
  "DoublonsMonoGravite": "Mineure",
  "MaxPeriodDays": 366,
  "CriticalRatioBlocking": 0.05,
  "Gravites": { "FerieNonApplique": "Importante" },
  "Poids": { "Information": 0, "Mineure": 1, "Importante": 3, "Critique": 5 }
}
```

### Niveau « Information » et gravité des doublons

`Information` (poids 0) est un constat sans effet sur la paie : il figure dans les **comptages** et,
sur demande (`includeInformation=true`, case à cocher de l'écran), dans la **liste** ; il n'entre ni
dans le score (poids 0 par défaut) ni dans le statut (qui ne dépend que des critiques). Le poids reste
réglable (`Closing:Poids:Information`) : un poids positif le ferait compter dans le score.

Gravité d'un doublon exact ou d'un quasi-doublon, par ordre de priorité :

1. **la gravité fixée dans `Closing:Gravites:<Type>`** gagne dans les deux modes de pointage ;
2. sinon, **multi-pointage** (`T_CLOCK.MULTIPOINT = 1`) : `Information` (la paie ignore le double passage) ;
3. sinon, **mono-pointage** : `Closing:DoublonsMonoGravite` (`Mineure`) : le double passage peut fausser les heures.

Par défaut le férié non appliqué est `Information` : la paie ne lit jamais les drapeaux
`FERIER` / `M_FERIES`, donc leur absence n'a pas d'effet sur la paie. Son lien « Corriger » ouvre la page
Anomalies (du = jour, au = jour + 1, matricule) seulement si un pointage existe ce jour-là
(`PointageExiste`) ; sinon l'écran écrit « Rien à corriger dans l'application ».

### Salariés sans planning

Avec `ExclureSansPlanningSurPeriode = true`, un salarié qui n'a **aucune** ligne de planning sur la
période analysée n'est pas contrôlé par « pointage sans planning » ; un salarié planifié ne serait-ce
qu'un jour reste contrôlé. Le résultat expose leur nombre (`salariesSansPlanningExclus`, les salariés
exclus qui ont au moins un pointage sur la période) et l'écran écrit « N salariés sans aucun planning sur
la période ne sont pas contrôlés ». Leurs jours restent comptés dans les **jours-employé contrôlés**
(définition inchangée) : ils pèsent donc sur le dénominateur du score sans pouvoir y ajouter de poids.

### Revenir exactement aux anciens réglages

Les valeurs d'avant la phase 3c, à placer dans la section `Closing` (le résultat est alors identique
à l'ancien, hormis le libellé du détail des doublons et des fériés) :

```json
"Closing": {
  "DuplicateToleranceSeconds": 120,
  "ExclureSansPlanningSurPeriode": false,
  "CriticalRatioBlocking": 0.02,
  "Gravites": {
    "DoublonExact": "Mineure",
    "QuasiDoublon": "Mineure",
    "FerieNonApplique": "Importante"
  }
}
```

`Information` n'est alors plus utilisé (aucune anomalie à ce niveau). Les gravités explicites de
`DoublonExact` et `QuasiDoublon` rétablissent `Mineure` quel que soit le mode de pointage.

Types d'anomalie (`<Type>`) : `PointageIncomplet`, `PointageManquant`, `PlanningSansPointage`,
`PointageSansPlanning`, `DoublonExact`, `QuasiDoublon`, `FerieNonApplique`.

## Tâches courantes

- **Changer un seuil** (tolérance, ratio bloquant, période maximale) : la clé correspondante de la
  section `Closing`. Pour changer la valeur par défaut elle-même : `ClosingOptions.cs`
  (`ClosingRulesOptions.DefaultDuplicateToleranceSeconds` et
  `ClosingStatusRules.DefaultCriticalRatioBlocking` pour le ratio).
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
