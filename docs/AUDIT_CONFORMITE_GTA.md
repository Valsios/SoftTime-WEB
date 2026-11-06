# Audit de conformité GTA — SoftTime-WEB

> Grille appliquée : `checklist-conformite-GTA.md` (référentiel Madagascar : 40 h/semaine, 173,33 h/mois, HS +30 % de la 41e à la 48e heure puis +50 %).
> Les valeurs légales restent **à faire valider par la RH / la paie** avant la recette.

**Application testée :** SoftTime-WEB (backend .NET 8 + front Angular)  **Version :** branche `fix/security` @ `31a3e4c`  **Date :** 06/11/2026  **Testeur :** audit de code assisté (Claude)

Légende : ✅ conforme · ❌ non conforme · ⚠️ partiel · N/A non applicable

**Méthode.** Lecture du code (moteur `OvertimeCalculator`, `OvertimeRules`, `OvertimeDayRules`, `OvertimeService`, `PunchPairingRules`, `PunchDayGrouping`, `PunchProcessingService`, `TimekeepingService`, `LeaveRules`, `SageLeaveCalendar`, `ClosingService`, écrivains paie, intercepteur d'audit, droits). Les jeux de test T1–T15 ont été **exécutés** sur le moteur de calcul (test xUnit temporaire, supprimé ensuite), dans les deux modes : *multi-pointage* (`T_CLOCK.MULTIPOINT = 1`, mode de référence) et *2 pointages* (mono).

### Où sont les règles

| Règle | Emplacement | Paramétrable ? |
|---|---|---|
| Base hebdo (40 h) | `T_CATEGORIE.HEURESEMAINE` (défaut `OvertimeRules.DefaultWeeklyHours`) | Oui, par catégorie |
| Journée théorique | `HEURESEMAINE / 5` (`OvertimeWeekConfig.Journee`) | Non (diviseur 5 en dur) |
| Tranche HS30 = 8 h | `OvertimeRules.WeeklyHs130Cap = 8` | **Non** (constante) |
| Plafond exonéré 20 h | `OvertimeRules.ExemptCeilingHours = 20` | **Non** (constante) |
| Plage de nuit 22 h–5 h | `OvertimeRules.NightStart / NightEnd` | **Non** (constantes) |
| Seuil de pause 5 h, pause par défaut 30 min | `OvertimeRules.PauseThreshold / DefaultPause` | Non |
| Priorité nuit / dimanche / férié | `T_MAJORATION.Cotation` | Oui (priorité, pas un taux) |
| Taux de majoration (130 %, 150 %, nuit…) | Constantes de paie SAGE (`T_CODE_CONSTANTE` → codes SAGE) | Côté paie, pas dans la GTA |
| Semaine lundi → dimanche | `OvertimeRules.IsMondayToSundayWeek` | **Non** |
| Tolérance d'entrée, pas d'arrondi de sortie | `T_TOLERANCE` (par catégorie) | Oui |

---

## 1. Paramétrage (aucune règle codée en dur)

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 1.1 | La durée légale hebdomadaire (40 h) se règle par paramètre | ✅ | `T_CATEGORIE.HEURESEMAINE`, par catégorie ; 40 h par défaut. |
| 1.2 | La base mensuelle (173,33 h) se règle par paramètre | N/A | La GTA ne mensualise pas ; c'est la paie SAGE qui le fait. |
| 1.3 | Le seuil journalier d'HS (8 h) peut être activé ou désactivé | ❌ | Pas d'interrupteur. Le mode est déduit de `MULTIPOINT` : en multi les HS sont hebdomadaires ; en mono elles sont **journalières** (dépassement du planning) sans seuil hebdo. |
| 1.4 | Les bornes des tranches d'HS se règlent | ❌ | `WeeklyHs130Cap = 8` et `ExemptCeilingHours = 20` sont des constantes, ainsi que le `12` codé en dur dans `AllocateExempt`. |
| 1.5 | Les taux de majoration se règlent pour chaque tranche | ⚠️ | Les taux ne sont pas dans la GTA : elle exporte des **heures** par rubrique (EXO130, EXO150, I130, I150) et SAGE applique ses propres taux. Les rubriques se règlent (`T_CODE_CONSTANTE`). |
| 1.6 | Les taux de nuit, de dimanche et de jour férié se règlent séparément | ⚠️ | Même principe : des rubriques séparées (Nuit, Dimanche, Férié) sont exportées ; `T_MAJORATION.Cotation` règle seulement la **priorité** entre elles. |
| 1.7 | La plage de nuit se règle | ❌ | 22:00 → 05:00 en dur (`OvertimeRules.NightStart/NightEnd`). |
| 1.8 | Une règle de cumul des majorations est définie | ⚠️ | Règle définie et documentée : entre nuit, dimanche et férié, **la cotation la plus forte l'emporte** ; HS et majorations se cumulent. Seul l'ordre est réglable, pas le mode (addition, plus fort…). |
| 1.9 | Le premier jour de la semaine de référence se règle | ❌ | Lundi imposé (`EnsureMondayToSunday`). |
| 1.10 | Les paramètres sont datés (date d'effet) et historisés | ❌ | Aucune date d'effet sur `T_CATEGORIE`, `T_TOLERANCE`, `T_MAJORATION` ; seul le journal d'audit garde l'ancienne valeur. |
| 1.11 | Un changement de paramètre ne recalcule pas les périodes clôturées | ⚠️ | Les semaines **validées** sont figées (`T_HS_VALIDER` n'est plus recalculée). Les semaines non validées et l'absence de la période (recalculée par `CalculateExoImpoAsync`) prennent le paramétrage du moment. |
| 1.12 | Le calendrier des jours fériés se gère par année | ✅ | `T_FERIE` = dates complètes, CRUD `HolidaysController`. Pas de reconduction automatique des fériés fixes. Règle du 8 mars (femmes) gérée en dur. |

## 2. Horaires et heures théoriques (HT)

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 2.1 | Chaque salarié est rattaché à un horaire à toute date | ⚠️ | `T_PLANNING_SAL` par jour. S'il manque, le planning est **reconstruit avec les heures réelles** (`OvertimeDayRules.Construire`) : pas de retard, pas d'absence et, en mono, aucune HS ce jour-là. Le contrôle avant paie le signale (`PointageSansPlanning`), sauf pour les salariés sans aucun planning sur la période (`ExclureSansPlanningSurPeriode = true`). |
| 2.2 | L'HT journalière et hebdomadaire est calculée à partir du planning | ⚠️ | La journée théorique vaut `HEURESEMAINE / 5` et non le planning. Le champ `HT` de `T_HS_VALIDER` contient les heures **créditées** (travaillées, congés, fériés), pas des heures théoriques. |
| 2.3 | Cycles (2x8, 3x8) et horaires de nuit gérés | ⚠️ | Shifts (`NoSHIFT`), génération de planning et postes de nuit (`PunchDayGrouping`). **Anomalie bloquante sur les postes de nuit avec pause après minuit** : voir T6. |
| 2.4 | Temps partiels gérés avec leur propre base contractuelle | ❌ | La base contractuelle (`HEURESEMAINE`) sert de **seuil d'HS** : 28 h pour 24 h de contrat donnent 4 h HS30 au lieu d'heures complémentaires (T14). |
| 2.5 | Pauses rémunérées et non rémunérées distinguées | ❌ | Une seule notion de pause, toujours déduite (multi : seulement la part manquante, au-delà de 5 h travaillées). |
| 2.6 | Jour de repos hebdomadaire défini (24 h consécutives min.) | ⚠️ | `OffP` sur le planning ; aucun contrôle des 24 h consécutives. |
| 2.7 | Un jour férié réduit l'HT ou est traité selon le paramétrage | ✅ | Férié non travaillé : une journée est créditée ; travaillé : heures comptées en « Férié » (T7). Comportement fixe, non paramétrable. |

## 3. Pointages et heures de présence

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 3.1 | Les pointages bruts sont conservés tels quels | ❌ | `TimekeepingService.UpdatePunchAsync` **modifie `T_POINTAGE` sur place** (date, heure, type). L'ancienne valeur ne survit que dans `suivi_JOURNAL`. Les purges archivent dans `T_HISTORIQUE_POINTAGE`. |
| 3.2 | Les pointages sont appariés correctement | ✅ | `PunchPairingRules` (mono : premier/dernier ; multi : seuil de 15 min, tri déterministe). Tests unitaires présents. |
| 3.3 | Pointage manquant ou en double signalé, sans calcul silencieux | ⚠️ | Nombre impair → anomalie, aucune heure ; jour planifié sans pointage → anomalie d'absence ✅. En multi, les pointages à moins de 15 min sont **écartés en silence** au pré-traitement (le contrôle avant paie les relève ensuite comme doublons). |
| 3.4 | Poste de nuit rattaché au bon jour | ⚠️ | Rattachement au jour de début (fin planifiée + 4 h de marge). Non paramétrable (pas de découpage possible). Le calcul perd les heures après minuit quand le poste a une pause (T6). |
| 3.5 | Correction manuelle : motif, auteur, date, ancienne valeur | ⚠️ | Auteur, date et ancienne valeur sont dans le journal EF (`AuditJournalSaveChangesInterceptor`) ✅. **Aucun motif** n'est demandé ❌. Au-delà de 200 lignes par table et par sauvegarde (`AuditJournal:DetailThreshold`), le journal ne garde qu'un **résumé** sans valeurs. |
| 3.6 | Règles d'arrondi appliquées et documentées | ⚠️ | Sortie arrondie **vers le bas** au pas `T_TOLERANCE.SORTIE` (multi) ou à la demi-heure (mono, en dur). Arrivée en avance non payée (plancher à l'heure planifiée). Pas d'arrondi d'entrée. Documenté dans le code seulement. |
| 3.7 | Tolérances de retard et de départ anticipé appliquées | ⚠️ | Tolérance d'entrée appliquée **en franchise** (retard = entrée − (HA + tolérance)). **Le départ anticipé n'est pas calculé.** |
| 3.8 | Fuseau horaire et changement d'heure gérés | N/A | Heures locales de la pointeuse, sans conversion. Acceptable à Madagascar (UTC+3, pas d'heure d'été) avec une seule zone ; à revoir pour des sites multi-fuseaux. |

## 4. Heures travaillées effectives

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 4.1 | Heures effectives = présence − pauses non rémunérées | ✅ | Multi : seule la part de pause **non prise** est déduite, et seulement au-delà de 5 h travaillées. Mono : la pause est toujours déduite. |
| 4.2 | Chaque heure classée : normale, nuit, dimanche, férié | ⚠️ | Correct en multi. **En mono, la nuit est fausse pour un poste qui passe minuit** : 20 h → 4 h donne 2 h de nuit au lieu de 6 h (pas de découpe à minuit). |
| 4.3 | Écart effectif / théorique calculé chaque jour | ⚠️ | Écran de détail : `heuresSupJour = max(0, retenu − journée)`. Écart négatif seulement sous la forme retard ou absence. |
| 4.4 | Écart négatif classé (retard, départ anticipé, absence) | ⚠️ | Retard ✅, absence ✅, départ anticipé ❌. |
| 4.5 | Écart positif classé « dépassement » en attente de validation | ⚠️ | Pas de catégorie « dépassement ». La semaine calculée reste « non validée » jusqu'à validation et n'est pas exportée avant (voir 6.5). |

## 5. Absences

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 5.1 | Chaque type d'absence est paramétré | ⚠️ | `T_CODEABSENCE` (libellé, `NOT_PAY`) existe, mais **le moteur HS ne s'en sert pas**. Tous les événements SAGE, sauf les codes `0200` et `0300`, sont traités comme congé (`LeaveRules.EstCodeConge`), y compris une éventuelle maladie. |
| 5.2 | Paramètre « l'absence compte comme temps de travail pour le seuil HS » | ❌ | Un congé crédite toujours une journée (`Journee × valeur`) dans le total hebdo : il compte donc **toujours** pour le seuil (T5 donne 8 h HS30). Rien ne permet de changer ce comportement. |
| 5.3 | Congés payés acquis à 2,5 j/mois | N/A | Géré par la paie SAGE, pas par la GTA. |
| 5.4 | Absence + pointages le même jour signalé comme incohérent | ❌ | En multi, un jour de congé avec des pointages est **neutralisé en silence** (heures ignorées). Le croisement congé SAGE / pointage est explicitement reporté dans `PayrollClosing.cs`. |

## 6. Heures supplémentaires (HS)

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 6.1 | Les HS sont calculées par semaine | ⚠️ | ✅ en **multi** (total hebdo − `HEURESEMAINE`). ❌ en **mono** : somme des dépassements journaliers et samedi entièrement en HS, **même si la semaine n'atteint pas 40 h** (36 h travaillées donnent 4 h HS ; tests TX et TY). |
| 6.2 | Au-delà de 40 h dans la semaine = HS | ⚠️ | Oui en multi, par rapport à `HEURESEMAINE` de la catégorie (voir 2.4 pour les temps partiels). En mono, non garanti (voir 6.1). |
| 6.3 | Pas de double comptage journalier + hebdomadaire | ✅ | Un seul mode à la fois (T4 donne 2 h dans les deux modes). L'indicateur journalier de l'écran de détail n'est pas payé. |
| 6.4 | Tranches 41e–48e à +30 %, au-delà à +50 % | ✅ | `SplitWeekly` : 8 h en HS1, le reste en HS2 (T2 : 8 h + 3 h). Puis ventilation exonéré / imposable avec un plafond de 20 h (`AllocateExempt`). |
| 6.5 | Un dépassement non validé n'est pas payé comme HS | ✅ | `CalculateExoImpoAsync` ne retient que `VALIDATION_HS = true`. La validation porte toutefois sur **toute** la semaine (pas de validation partielle d'un nombre d'heures). |
| 6.6 | Validation tracée (auteur, date, heures validées) | ⚠️ | `T_HS_VALIDER` n'a ni auteur ni date de validation. La trace n'existe que dans le journal EF (utilisateur, horodatage, changement de `VALIDATION_HS`). |
| 6.7 | Semaine à cheval sur deux mois rattachée selon une règle constante | ✅ | Une semaine appartient à la période qui contient **son dimanche** (`BornesSemainesDeLaPeriode`), sans doublon. Réouverture refusée si la semaine est déjà ventilée. |
| 6.8 | Compteur annuel d'HS avec alerte au contingent | ❌ | Aucun compteur annuel. Le plafond de 20 h est un plafond d'exonération **par période** de calcul. |
| 6.9 | Plafond de durée maximale (jour / semaine) avec alerte | ❌ | Absent. |
| 6.10 | Repos compensateur / récupération | ❌ | Absent (à confirmer si l'entreprise le prévoit). |

## 7. Majorations spéciales

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 7.1 | Heures de nuit détectées selon la plage paramétrée | ⚠️ | Détection correcte en multi, mais plage en dur. Mono faux après minuit (voir 4.2). |
| 7.2 | Travail du dimanche majoré | ⚠️ | Compteur Dimanche exporté ✅. En mono, les heures du dimanche **ne comptent pas dans les HS** (T15 mono : 0 HS au lieu de 6). Le « jour de repos » d'un salarié qui ne se repose pas le dimanche n'est pas majoré. |
| 7.3 | Travail d'un jour férié majoré | ✅ | Compteur Férié exporté (T7). |
| 7.4 | Règle de cumul appliquée comme paramétré | ⚠️ | Conforme à la règle du code (voir 1.8) ; le mode de cumul n'est pas paramétrable. |
| 7.5 | Nuit habituelle et nuit occasionnelle distinguées | ❌ | Non. |

## 8. Clôture et export vers la paie

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 8.1 | Période clôturable puis rouvrable avec traçabilité | ⚠️ | Pas d'objet « période clôturée ». Le verrou repose sur la validation de semaine et l'existence du calcul EXO/IMPO : purge préalable obligatoire, archivage `T_HISTORIQUE_HS`, journal EF. Le « contrôle avant paie » (`ClosingService`) est une analyse en lecture seule. |
| 8.2 | Une rubrique par nature et par taux | ⚠️ | Exportés : EXO130, EXO150, I130, I150, Férié, Dimanche, Nuit. **Absence et retard ne sont pas exportés** (calculés et affichés seulement). |
| 8.3 | Totaux exportés = totaux affichés | ⚠️ | Mêmes lignes `T_HSExoImp`, mais les salariés sans `NumSal` SAGE sont **ignorés en silence** à l'export (`rows.Where(h => h.NumSal.HasValue)`), sans liste des rejets. |
| 8.4 | Régularisations sur période clôturée exportées comme rappel | ❌ | L'export **écrase** la valeur courante (`UPDATE T_CUMSAL SET …`). Il n'y a ni rappel ni différentiel. |
| 8.5 | Format d'export documenté et versionné | ⚠️ | Rubriques configurables (`T_CODE_CONSTANTE`, mappings de la base AUTRE) ; aucune spécification versionnée. Chaque synchronisation est journalisée (`SYNC_PAIE`). |

## 9. Traçabilité, sécurité et obligations

| # | Point de contrôle | Statut | Commentaire |
|---|---|---|---|
| 9.1 | Journal d'audit sur pointage, planning, paramètre, validation | ✅ | Intercepteur EF transactionnel : avant/après par colonne, utilisateur, données sensibles masquées. Limite : un simple résumé au-delà de 200 lignes par table (voir 3.5). |
| 9.2 | Droits séparés : salarié, manager, RH, administrateur | ⚠️ | Rôles et permissions configurables (portes 1 à 6, `T_ROLE_PERMISSION`, audit des droits). Mais une seule porte « Traitement » couvre le pré-traitement, la validation des anomalies et la validation des HS. Aucun profil salarié. |
| 9.3 | Un salarié ne peut pas valider ses propres HS | ❌ | Le compte utilisateur n'est pas relié à un matricule : aucun contrôle possible. |
| 9.4 | Registre des heures et des congés éditable | ⚠️ | Rapports (pointages, retards, absences, HS) et exports Excel. Pas de « registre » formalisé pour l'inspection. |
| 9.5 | Durée de conservation paramétrable, sauvegardes | ⚠️ | Archives (`T_HISTORIQUE_*`), mais pas de durée de rétention paramétrable ; les sauvegardes relèvent de l'exploitation SQL Server. |
| 9.6 | Le salarié peut consulter ses propres compteurs | ❌ | Pas d'espace salarié. |

---

## 10. Jeux de test (résultats obtenus)

Conditions : `HEURESEMAINE = 40`, pause 0 (« pauses déjà déduites »), planning 08:00–16:00 sauf mention contraire. Résultats obtenus sur `OvertimeCalculator.CalculerSemaine` puis `OvertimeRules.SplitWeekly`.

| # | Scénario | Résultat attendu | Obtenu (multi) | Obtenu (mono) | Statut |
|---|---|---|---|---|---|
| T1 | 5 × 8 h | 40 h, 0 HS | 40 h, 0 HS | idem | ✅ |
| T2 | 5 × 9 h + samedi 6 h | 8 h HS30 + 3 h HS50 | 11 HS → 8 + 3 | 11 HS → 8 + 3 | ✅ |
| T3 | 4 × 8 h + vendredi absent | 32 h, 8 h d'absence, 0 HS | 32 h, absence 8 h, 0 HS | idem | ✅ |
| T4 | 10 h + 4 × 8 h, seuil journalier | 2 h HS30 (pas 4) | 2 h | 2 h | ✅ (pas de seuil journalier activable) |
| T5 | Congé lundi + 4 × 10 h | Selon 5.2 | 8 h HS30 (congé compté) | 8 h HS30 | ⚠️ non paramétrable |
| T6 | 20 h → 4 h, pause 0 h–0 h 30 | 7,5 h dont 5,5 h de nuit | **4 h, dont 2 h de nuit** | **4 h, dont 2 h de nuit** | ❌ |
| T7 | 8 h un jour férié | 8 h « férié » | Férié = 8 h, HT = 40 h | idem | ✅ |
| T8 | Entrée sans sortie | Anomalie, aucune heure | Anomalie `T_HEUREANOMALIE`, aucune correction (code) | idem | ✅ |
| T9 | 41 h non validées | 0 HS payée | 1 h calculée, non exportée tant que non validée | idem | ✅ (pas de statut « dépassement » distinct) |
| T10 | Entrée 8 h 07, tolérance 5 min | 7 min ou 15 min | **2 min** (franchise) | 2 min | ⚠️ règle à valider par la RH |
| T11 | 48 h du lundi 28 au dimanche 4 | 8 h HS30 sans doublon | Rattachée au mois du dimanche | idem | ✅ |
| T12 | Contingent annuel atteint | Alerte | Pas de compteur | idem | ❌ |
| T13 | Taux HS30 modifié au 1er du mois M | Ancien taux conservé | Taux hors GTA ; semaines validées figées | idem | N/A (vérifier côté SAGE) |
| T14 | Temps partiel 24 h, 28 h travaillées | 4 h complémentaires, pas des HS | **4 h HS30** | **4 h HS30** | ❌ |
| T15 | Dimanche 6 h dans une semaine à 40 h | 6 h HS30 + majoration dimanche | 6 HS30 + Dimanche 6 | **0 HS** + Dimanche 6 | ⚠️ (✅ multi, ❌ mono) |

Tests complémentaires de diagnostic :

| Cas | Multi | Mono | Constat |
|---|---|---|---|
| 20 h → 4 h en une seule paire | 8 h, dont 6 h de nuit ✅ | 8 h, dont **2 h** de nuit ❌ | Le mono ne découpe pas à minuit |
| Seconde paire 00:30 → 04:00 seule, planning HA 20:00 | **0 h** ❌ | **0 h** ❌ | Plancher « arrivée en avance » appliqué à tort |
| 4 × 9 h + vendredi absent (36 h) | 0 HS ✅ | **4 h HS** ❌ | Mono non hebdomadaire |
| 32 h + samedi 4 h (36 h) | 0 HS ✅ | **4 h HS** ❌ | Samedi toujours en HS en mono |

**Cause de T6.** Dans `OvertimeCalculator.Intervalles` (et dans la boucle de `JourMono`), une paire dont l'entrée et la sortie tombent le même jour et dont l'entrée précède `PlanningHa` voit son début ramené à `PlanningHa`. Pour la paire 00:30 → 04:00 d'un poste planifié à 20:00, le début devient 20:00, donc après la fin : la paire est ignorée. Il faut limiter ce plancher au premier intervalle du jour de début du poste, ou comparer des dates-heures complètes et non des heures seules.

---

## 11. Synthèse

| Section | ✅ | ⚠️ | ❌ | N/A |
|---|---|---|---|---|
| 1. Paramétrage | 2 | 4 | 5 | 1 |
| 2. Horaires et HT | 1 | 4 | 2 | 0 |
| 3. Pointages | 1 | 5 | 1 | 1 |
| 4. Heures effectives | 1 | 4 | 0 | 0 |
| 5. Absences | 0 | 1 | 2 | 1 |
| 6. HS | 4 | 3 | 3 | 0 |
| 7. Majorations | 1 | 3 | 1 | 0 |
| 8. Paie | 0 | 4 | 1 | 0 |
| 9. Traçabilité | 1 | 3 | 2 | 0 |
| 10. Jeux de test | 8 | 3 | 3 | 1 |

**Points bloquants avant mise en production :**

- **T6, postes de nuit avec pause après minuit** : les heures après minuit sont perdues (les deux modes). Le mono calcule aussi mal la nuit au-delà de minuit. Les montants payés sont faux.
- **Mode 2 pointages (mono) non conforme au calcul hebdomadaire** : HS journalières et samedi entièrement en HS, même sous 40 h ; le dimanche ne compte pas dans les HS. Il faut soit l'aligner sur le calcul hebdomadaire, soit l'interdire pour les clients soumis au référentiel.
- **Temps partiel** : les heures au-delà du contrat sont payées en HS30 au lieu d'heures complémentaires (T14).
- **Congés et absences** : tout événement SAGE (sauf 0200/0300) compte comme temps de travail pour le seuil d'HS, sans paramètre (5.2). Un congé avec pointages est neutralisé sans alerte (5.4).
- **Export paie** : l'export écrase la valeur courante sans rappel (8.4), ignore sans le dire les salariés sans `NumSal` (8.3) et n'exporte ni l'absence ni le retard (8.2).
- **Pointages bruts modifiables sur place** (3.1) et corrections sans motif (3.5).

**Réserves (à traiter ou à faire accepter par la RH / la paie) :**

- Constantes légales en dur : tranche de 8 h, plafond exonéré de 20 h, plage de nuit, lundi comme premier jour, journée = hebdo / 5, arrondi mono à 30 min (1.3, 1.4, 1.7, 1.9).
- Paramètres sans date d'effet (1.10).
- Tolérance d'entrée appliquée en franchise (T10) ; départ anticipé non calculé (3.7, 4.4).
- Aucun compteur annuel d'HS, aucun plafond de durée maximale (6.8, 6.9).
- Validation des HS sans auteur ni date dans `T_HS_VALIDER` (6.6) ; un salarié peut valider ses propres HS et n'a pas d'accès à ses compteurs (9.3, 9.6).

**Décision proposée :** ☐ Conforme ☐ Conforme avec réserves ☒ **Non conforme** (tant que les points bloquants restent ouverts)

**Validé par (RH / Paie) :** ____________  **Date :** ________
