# Évaluation d'utilité du Contrôle avant paie

État au 5 novembre 2026. Cette page ne contient que des comptages et des constats vérifiables (code ou mesures sur la base de démo).

## 1. Recouvrement avec l'écran Anomalies (base de démo, novembre 2025)

A = journées-salariés listées par l'écran Anomalies (`T_HEUREANOMALIE`), B = lignes du Contrôle avant paie. La clé de rapprochement est (matricule, jour) : « A et B » veut dire que la même journée figure des deux côtés, pas que la cause est la même. Un même jour peut porter plusieurs lignes B.

**Précaution de lecture.** La démo (`SOFTTIME_DEMO_CLOTURE`) contient 48 anomalies injectées (15 pointages manquants, 10 incomplets, 8 doublons exacts, 6 quasi-doublons, 5 pointages sans planning, 4 fériés non appliqués). La colonne « B seulement » mesure donc ce que le contrôle sait détecter, pas la fréquence naturelle de ces anomalies. Les injections restent toutefois minoritaires : elles expliquent au plus 48 des 1 282 lignes du contrôle.

| Type du contrôle | Lignes B | A et B | B seulement | Part « A et B » | Injectées (max. dans B) |
|---|---|---|---|---|---|
| PointageIncomplet | 265 | 260 | 5 | 98 % | 10 |
| PointageManquant | 269 | 0 | 269 | 0 % | 15 |
| PlanningSansPointage | 3 | 0 | 3 | 0 % | 0 |
| PointageSansPlanning | 172 | 31 | 141 | 18 % | 5 |
| DoublonExact | 26 | 11 | 15 | 42 % | 8 |
| QuasiDoublon | 537 | 71 | 466 | 13 % | 6 |
| FerieNonApplique | 10 | 6 | 4 | 60 % | 4 |
| **Total** | **1 282** | | | | **48** |

Totaux : B = 1 282 lignes sur 1 213 journées-salariés ; journées communes avec A = 360 ; A = 3 953 journées-salariés, toutes de type « pointage » (aucune anomalie d'absence : 0 sur 3 953) ; **A seulement = 3 593** (absence : 0, pointage : 3 593).

Limites :
- L'injection a supprimé des pointages sans lancer « Traiter les pointages » : l'écran Anomalies ne connaît donc pas ces cas (0 anomalie d'absence côté A). Le « 0 » de PointageManquant dit que l'écran ne liste pas ces jours dans cette base, pas qu'il ne saurait pas les produire après traitement.
- Les 3 593 journées « A seulement » ne sont pas expliquées : le contrôle ignore par défaut les semaines validées et les jours déjà corrigés manuellement, mais la part de chaque cause n'a pas été comptée.
- Les durées de l'analyse (ML désactivé puis activé) ont été mesurées par le script de mesure : analyse complète de 1 à 3 s, page 2 sans cache de 1 à 3 s, page 2 avec cache environ 30 ms.

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
Temps réel de l'analyse : voir la section 1 (durées mesurées, aucun chronomètre de parcours manuel).

## 4. Lecture et décisions

Lecture des comptages (à confirmer avec le manager) :
- **PointageIncomplet** : 98 % des lignes tombent sur une journée déjà listée par l'écran Anomalies, et le code utilise les mêmes règles d'appariement. Doublon probable. Option : le garder comme simple lien vers l'écran Anomalies, ou le retirer.
- **PointageManquant, PlanningSansPointage, QuasiDoublon** : aucun recouvrement notable (0 %, 0 %, 13 %) et aucun écran équivalent dans le code. Valeur ajoutée établie.
- **PointageSansPlanning** : 18 % de recouvrement et aucun écran équivalent dans le code : valeur ajoutée.
- **DoublonExact** (42 %) et **FerieNonApplique** (60 %, niveau Information) : recouvrement partiel, détection partielle ailleurs. À trancher selon l'usage.

Aucune règle métier n'est modifiée par ce document. La décision de garder, réduire en lien ou retirer un type revient au manager.
