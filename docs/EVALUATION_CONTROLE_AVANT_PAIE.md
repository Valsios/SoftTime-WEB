# Évaluation d'utilité du Contrôle avant paie

État au 5 novembre 2026. Cette page ne contient que des comptages et des constats vérifiables dans le code.
**Les mesures sur la base de démo (section 1) ne sont pas encore faites** : elles sont marquées « à mesurer ».

## 1. Recouvrement avec l'écran Anomalies — à mesurer

A = journées listées par l'écran Anomalies (`T_HEUREANOMALIE`), B = lignes du Contrôle avant paie, mois de novembre 2025.

| Type du contrôle | A seulement | B seulement | A et B |
|---|---|---|---|
| PointageIncomplet | à mesurer | à mesurer | à mesurer |
| PointageManquant | à mesurer | à mesurer | à mesurer |
| PlanningSansPointage | à mesurer | à mesurer | à mesurer |
| PointageSansPlanning | à mesurer | à mesurer | à mesurer |
| DoublonExact | à mesurer | à mesurer | à mesurer |
| QuasiDoublon | à mesurer | à mesurer | à mesurer |
| FerieNonApplique | à mesurer | à mesurer | à mesurer |

## 2. Un écran existant détecte-t-il déjà le type ? (lecture du code)

| Type | Détecté ailleurs ? | Appui (fichier) |
|---|---|---|
| PointageIncomplet | Oui, mais seulement après « Traiter les pointages » | `PunchProcessingService.cs` : pointage orphelin → anomalie (écran Anomalies). Le contrôle utilise les mêmes règles d'appariement (`PayrollClosingRules.cs`, `PointagesIncomplets`). |
| PointageManquant | Oui | `PunchProcessingService.cs:307` (jour travaillé sans pointage → anomalie d'absence) et état « Absences non justifiées » (`ReportService.cs:67-85`, `AbsenceReportRules`). |
| PlanningSansPointage (demi-journée non couverte) | Non | `AbsenceReportRules` calcule ces lignes, mais l'état des absences ne garde que le type « Non justifiée » (`ReportService.cs:83`). |
| PointageSansPlanning | Non | Aucun écran ni service hors clôture ne compare les pointages au planning (`PayrollClosingRules.cs`, `PointagesSansPlanning`). |
| DoublonExact | Partiellement | L'import dédoublonne à l'entrée (`TimekeepingService.cs:262`, `:333`) ; aucun écran ne détecte un doublon présent après coup. |
| QuasiDoublon | Non | Règle propre à la clôture (`PayrollClosingRules.cs`, `QuasiDoublons`, tolérance en secondes). |
| FerieNonApplique | Partiellement | Le traitement pose l'indicateur férié au moment du calcul (`PunchProcessingService.cs`, `AjouterCorrection`) ; aucun écran ne signale un férié ajouté ensuite. Niveau « Information ». |

Lecture provisoire (à confirmer par les mesures de la section 1) : trois types n'ont aucun équivalent (PlanningSansPointage, PointageSansPlanning, QuasiDoublon), deux n'en ont que la moitié (DoublonExact, FerieNonApplique), deux recoupent l'écran Anomalies ou l'état des absences (PointageIncomplet, PointageManquant). Aucun type n'est retiré tant que le recouvrement n'est pas compté.

## 3. Protocole « avec / sans » (préparation de paie de fin de mois)

Comptage minimal à partir du menu, sans chronomètre.

| | Sans le contrôle | Avec le contrôle |
|---|---|---|
| Écrans à ouvrir | 5 : Pointages, Anomalies, Heures corrigées, Rapports (absences), Planning salariés | 1 : Contrôle avant paie |
| Clics minimum | 10 (1 entrée de menu + 1 recherche par écran) | 2 (1 entrée de menu + « Analyser ») |
| Catégories couvertes | 4 sur 7, en tout ou en partie (voir section 2) | 7 sur 7 |

Les clics pour choisir la période ou le type d'état dans Rapports ne sont pas comptés : le chiffre « sans » est donc un minimum.
Temps réel de l'analyse : à mesurer (script de mesure fourni séparément, durées et comptages seulement).

## 4. Verdict

Non rendu. Il dépend des sections 1 et 3 mesurées. Pour un type en doublon avéré : le garder comme simple lien vers l'écran qui le traite, ou le retirer, à décider avec le manager.
