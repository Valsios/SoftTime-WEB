# Soft Time — Fonctionnalités essentielles

Soft Time est une application web de **gestion des temps et des heures supplémentaires**.  
Elle relie trois univers : la base **SoftTime**, les connexions **SAGE** (paie) et les bases **pointeuse** (badges / CHECKINOUT).

L’accès est filtré par **rôle** (6 droits) et par **société SAGE** active (en-tête `X-Sage-Database`).

---

## 1. Authentification et contexte

- Connexion JWT (utilisateurs `T_RESPONSABLE`) : jeton d'accès de **30 min** (`Jwt:AccessTokenMinutes`, aucune
  marge d'horloge) et **jeton de rafraîchissement** de 7 jours (`Security:RefreshTokenDays`), renouvelé à chaque
  usage (`POST /api/auth/refresh`) et révoqué à la déconnexion (`POST /api/auth/logout`). Seule son empreinte
  SHA-256 est stockée (`T_REFRESH_TOKEN`, migration 004). Un jeton déjà remplacé qui revient (vol probable) ferme
  toutes les sessions du compte ; un changement de mot de passe aussi. Le client rafraîchit seul la session sur un 401.
- **Verrouillage** : après 5 échecs consécutifs (`Security:MaxFailedLoginAttempts`), le compte est bloqué 15 min
  (`Security:LockoutMinutes`, réponse 403) ; un nouveau mot de passe défini par un administrateur le débloque.
  Connexion, rafraîchissement et création initiale sont limités à 20 requêtes/min par adresse IP (429).
- **Hachage BCrypt** (coût 12). Les anciens hachages PBKDF2 et les mots de passe historiques en clair sont
  convertis en BCrypt à la connexion suivante.
- **Secrets** : aucune chaîne de connexion ni clé JWT dans `appsettings.json`. En développement :
  `dotnet user-secrets set "ConnectionStrings:SoftTime" "..."` et `dotnet user-secrets set "Jwt:Key" "..."`
  (dans `SoftTime.Api`) ; ailleurs, variables d'environnement `ConnectionStrings__SoftTime` et `Jwt__Key`.
  L'API refuse de démarrer sans clé JWT d'au moins 32 caractères.
- **HTTP** : CORS limité aux origines de `Cors:AllowedOrigins` ; HSTS et redirection HTTPS hors développement ;
  en-têtes `X-Content-Type-Options`, `X-Frame-Options`, CSP ; Swagger exposé en développement seulement.
- **Démarrage initial** : tant qu'aucun compte n'existe, l'écran de connexion bascule sur la création du
  premier **administrateur** (`GET /api/auth/is-first-run`, `POST /api/auth/create-admin`). Le serveur
  refuse toute seconde création.
- **E-mail** : la création ou la modification d'un compte peut envoyer les identifiants par SMTP
  (section `Email` de `appsettings.json`). Tant que cette section est incomplète, **aucun envoi n'est
  tenté** : les identifiants sont à communiquer directement (le compte est créé normalement).
- **Mot de passe** : toute création ou modification de mot de passe exige au moins 8 caractères,
  une majuscule, une minuscule, un chiffre et un caractère spécial. La politique est appliquée par
  le serveur (`PasswordPolicy`) et exposée par `GET /api/auth/password-policy` ; les écrans de
  démarrage et Utilisateurs affichent les règles en direct avant l'enregistrement. Les champs mot
  de passe proposent un bouton œil pour afficher/masquer la saisie.
- Choix de la **base SAGE** et de la **base pointeuse** dans la barre supérieure.
- Droits : Bases de données, Paramètres, Traitement, Divers, Traçabilité, États.

---

## 2. Bases de données

- **Bases SAGE** : serveur, catalogue, authentification SQL.
- **Bases pointeuse** : même principe, base active pour l’import des pointages.
- **Accès bases** : quelles sociétés SAGE un utilisateur a le droit d’utiliser.

---

## 3. Paramètres

- **Utilisateurs, rôles, privilèges**.
- **Paramètres pointeuse** (ex. multi-pointeuse).
- **Correspondance** : mapping automatique ou manuel SAGE ↔ badge.
- **Correspondances paie** (`T_CARDPAIE`) : matricule SAGE, nom, badge ; auto-mapping ; import CSV.
- **Catégories**, **affectations** salariés, **tolérances** d’horaires.
- **Jours fériés**, **majorations**, **codes absence** (sync SAGE `T_GHR`).
- **Code constante** : codes paie HS (EXO/IMPO, férié, dimanche, nuit) utilisés à la synchro SAGE.

---

## 4. Traitement (cœur métier)

| Module | Rôle |
|---|---|
| **Shifts** | Catalogue des horaires (entrée, pause, sortie). Import / export Excel. |
| **Planning salariés** | Affectation d’un shift par jour ; import Excel (matricule ou catégorie). |
| **Pointages** | Liste, import pointeuse, import Excel. |
| **Anomalies** | Heures anormales détectées. |
| **Heures corrigées** | Saisie / import des corrections (entrée, sortie, absences, etc.). |
| **Validation semaine** | Calcul HT/HS lundi–dimanche, valider ou annuler. |
| **Heures supp.** | Calcul EXO/IMPO (plafond), purge, **sync SAGE** (`T_CUMSAL`). |

**Règles de pré-traitement des pointages** (portage de `Frm_Import.importa` du desktop) :

- **Mono-pointage** (`T_CLOCK.MULTIPOINT = false`) : regroupement par jour calendaire ; premier pointage = entrée, dernier = sortie. `0` pointage sur un jour ouvré → anomalie d’absence ; `1` → anomalie `H1`/`H2` ; `≥ 2` → correction.
- **Multi-pointage** : journée simple = pointages du jour ; poste de nuit = pointages rattachés au jour d’entrée jusqu’à la sortie planifiée, les pointages de sortie étant consommés pour ne pas être retraités le lendemain ; appariement avec seuil de 15 minutes, dernier pointage forcé en sortie ; nombre pair → corrections, impair → une anomalie par jour.
- **Fériés** : lus dans `T_FERIE` (synchronisé depuis SAGE `EtatJour = 1`). Un férié travaillé produit une correction avec `FERIER`, `FERIER_AM` et `FERIER_PM`.
- **Jours non travaillés** (samedi/dimanche, `OffP`, férié) : sans pointage → rien ; avec pointage → correction (travail exceptionnel).
- **Protections** : les corrections `ORIGINE = MANUEL` et les semaines validées ne sont jamais écrasées ; seules les corrections `ORIGINE = AUTO` **du périmètre traité** (département, plage de matricules, pointeuse) et hors semaine validée sont purgées puis régénérées.
- **Semaine validée figée** : saisie, modification, suppression et import d'heures corrigées, validation d'anomalies et modification de pointage sont refusés (l'import ignore la ligne) tant que la validation n'est pas annulée.
- **Arbitrage d'une absence** : une anomalie marquée absente matin **et** après-midi se valide sans heures (des heures saisies en plus sont refusées) ; elle devient une absence comptée dans `T_HS_VALIDER.ABSENCE` / `T_HSExoImp.ABSENCE`.
- **Import** : dédoublonnage systématique (pointeuse et Excel, dans les deux modes), puis pré-traitement enchaîné depuis l’écran Pointages. Les pointages bruts restent dans `T_POINTAGE`.

**Calcul des heures supplémentaires — validation semaine** (portage de `Frm_Validations.calculHSSemaine`, synthèse `SYNTHESE_CALCUL_HS.md` §4) :

- **Deux modes** selon `T_CLOCK.MULTIPOINT` (aucune ligne `T_CLOCK` ⇒ multi-pointage, mode de référence) :
  - **multi-pointage** : `HS = total hebdomadaire crédité − HEURESEMAINE` (catégorie, 40 h par défaut) ;
  - **2 pointages** : `HS = Σ max(0, heures du jour − prévu au planning)` ; le samedi est intégralement en HS.
- **Arrivées / sorties** : entrée en avance non payée (`max(entrée, HA)`) ; sortie arrondie **vers le bas** au pas
  `T_TOLERANCES.SORTIE` (multi) ou à la demi-heure inférieure (mono).
- **Pause** : `T_PLANNING_SAL.Pause` → `T_CATEGORIE.PAUSE` → 30 min. En multi, seule la part **manquante** est
  déduite et seulement au-delà de 5 h travaillées ; en mono elle est toujours déduite.
- **Nuit** : 22:00 → 05:00. Dimanche et férié ont leurs propres compteurs ; une heure ne reçoit qu'**une**
  majoration, la `T_MAJORATION.Cotation` la plus forte l'emporte.
- **Crédits** : un férié non travaillé crédite la journée (`HEURESEMAINE / 5`) ; un congé SAGE crédite la journée
  (ou la demi-journée) ; le 8 mars crédite la journée des salariées (`T_SAL.Civilite` renseignée et ≠ 0).
- **Absence** : jour planifié non chômé sans aucune heure de présence, hors congé et hors férié → `(HD − HA) − pause`.
  Une ligne qui ne fait que déclarer une absence (`ABSENCE_AM` / `ABSENCE_PM` sans heure) n'est pas une présence. Depuis le lot C3, une journée
  couverte **seulement** par de telles absences arbitrées n'est comptée que si l'une porte un code d'absence réglé « compté comme non justifié »
  (section 5 nonies) ; un jour planifié sans aucune ligne reste toujours compté.
- **Validation** : seules les lignes cochées passent `VALIDATION_HS = true` et verrouillent `VALIDER_HS` ; une
  semaine validée reste affichée (elle n'est plus recalculée) et peut être annulée, voire purgée — sauf si elle
  est déjà incluse dans un calcul EXO/IMPO : purger d'abord les heures supplémentaires de cette période.
- **Ventilation** : `T_HSSemaine` est reconstruit à chaque calcul à partir des semaines **validées** —
  8 h à 130 %, au-delà à 150 %.
- **Exonération** : `T_HSExoImp` agrège les semaines validées de la période (max. un mois) — une semaine appartient
  à la seule période qui contient son **dimanche** (une semaine à cheval sur deux mois n'est payée qu'une fois) —, plafond de 20 h
  exonérées consommé d'abord sur le 130 %, et reporte `NUIT`, `DIM`, `FERIE`, `RETARD` et `ABSENCE`.
- **Écarts assumés vs desktop** : pas d'écriture dans `T_HS_NONVALIDER` (`T_HS_VALIDER` est la source unique) ;
  l'exonération n'agrège que les semaines validées (`VALIDATION_HS`) ; nuit / dimanche / férié ne se cumulent
  pas ; congés et fériés exclus de l'absence ; civilité nulle ⇒ règle du 8 mars non appliquée ; l'arrondi de
  sortie est un vrai plancher (le desktop retirait un pas de trop sur les multiples exacts).

**Valeurs enregistrées pour le planning salarié** (alignées sur le desktop) :

- `DateP` = date de début du shift, toujours à minuit.
- `HA` = `DateP` + heure d’entrée ; `HD` = `DateP` + heure de sortie si la sortie est après l’entrée, sinon **lendemain** (poste de nuit). Exemple : `17:00 → 07:00` donne `HD = J+1 07:00`.
- `Pause` = `00:00:00` si aucune pause n’est définie.
- `OffP = true` : `HA`/`HD` à `NULL`, `Pause = 00:00:00`.
- Import Excel : une cellule `OFF` crée une ligne off ; une cellule vide est ignorée.
- La saisie manuelle, l’import Excel et la génération CSV utilisent la même règle de report de nuit.
- La clé web reste `IDSAL` (carte salarié) avec `NoSHIFT` ; le desktop utilise `IDCATSAL`. Les valeurs temporelles sont alignées, pas la structure de clé.
- Les bases existantes peuvent être réparées avec [`migrations/fix_planning_sal_night.sql`](softtime_backend/migrations/fix_planning_sal_night.sql) (sauvegarde préalable).

---

## 5. États (rapports)

Période + plage de matricules + département, export Excel. Les états sont **calculés** (aucune table
tampon n'est lue) et partagent leurs règles avec le tableau de bord analytique.

- État pointage, récap HS EXO/IMPO, heures par semaine, heures dimanche : lecture directe.
- **État absences** (`AbsenceReportRules`) — n'affiche **que les absences non justifiées** : une
  ligne par salarié et par jour, plafonné à la veille (aucun jour futur). Colonnes : matricule, nom,
  prénom, département, date, durée.
  - `Non justifiée` = jour planifié **avec heures** et **sans aucun pointage** (aucune heure
    corrigée ce jour-là) ; durée = `(HD − HA − pause)`.
  - **N'apparaissent pas** dans l'état (ils servent seulement à exclure le jour) : `Congé SAGE`
    (`T_GHRSAL` hors codes `0200` / `0300`), `Arbitrée GTA` (correction portant `ABSENCE_AM` /
    `ABSENCE_PM`, donc absence déclarée), samedi/dimanche, férié (`T_FERIE`), **jour de planning OFF**
    (`OffP = true`) et shift sans heures (`HA`/`HD` nuls, `HA = HD`).
- **État retards** (`RetardReportRules`) — une ligne par salarié et par jour :
  - seul le **début de journée** est contrôlé : première entrée du jour comparée à `HA` du planning
    + `T_TOLERANCES.TOLERANCE` de la catégorie (`TYPES_TOL = false`) ; la branche après-midi du
    desktop (`HPE` / 13:30) n'est pas portée et une absence du matin (`ABSENCE_AM`) annule le
    retard (pas de retard « de midi ») ;
  - `T_HEURECORRIGER.RETARD` saisi reste prioritaire ; retard **jamais négatif** ; sans planning ou
    sur un jour OFF, aucun retard calculé ; week-end, férié et jours futurs exclus ;
  - mono et multi-pointage donnent le même résultat (regroupement par jour, seule la première
    entrée compte, une ligne par jour).
- Congé SAGE du jour.

---

## 5 bis. Tableau de bord analytique (`/analytics`)

Période + **regroupement** (quotidien / hebdomadaire / mensuel) + filtres **département** et
**catégorie de personnel**. Le regroupement hebdomadaire suit la semaine ISO lundi→dimanche et
s'affiche « Semaine 27 (30/06 - 06/07/2025) » (année ISO, donc correcte au passage d'année).

- **Indicateurs** : 5 cartes — effectif actif, taux d'absentéisme (jours d'absence / jours
  planifiés), taux de ponctualité (jours pointés sans retard / jours pointés), cumul des heures
  supplémentaires, semaines validées. Les totaux secondaires (absences, retards, anomalies,
  corrections) sont sur une ligne de détail sous les cartes.
- **Courbes** : évolution des absences et des retards ; évolution des heures (travaillées /
  supplémentaires). Le tableau chiffré « Absences et retards par période » les suit.
- **Répartition** : onglets **Catégorie / Département** (barres, barres) avec
  l'activité de l'axe sélectionné (effectif actif, absences, retards, minutes, HS).
- **Assiduité** : top 5 des salariés les plus et les moins ponctuels de la période (matricule, nom,
  ponctualité, jours pointés, retards, absences).
- **Export PDF** : bouton **Export PDF** dans l'en-tête. Le document est l'impression du rapport
  affiché (A4 portrait, thème clair, sans menu ni filtres) ; il reprend les KPI, les courbes, la
  répartition de l'onglet actif et l'assiduité. Pendant l'export, la pagination des tableaux est
  désactivée pour imprimer **tout le jeu filtré**, pas seulement la page courante. Le navigateur
  ouvre sa boîte d'impression : choisir « Enregistrer au format PDF ». Le thème, le titre et la
  pagination de l'écran sont restaurés à la fermeture.
- **Export Excel** : bouton **Export Excel** dans l'en-tête. Classeur multi-feuilles `.xlsx` généré
  à partir des mêmes données affichées : **Synthèse** (filtres, KPI, totaux), **Périodes**
  (absences/retards par période + heures), **Répartition - Catégorie** et **Répartition -
  Département** (les deux axes, quel que soit l'onglet affiché) et **Assiduité**. Toutes les lignes
  filtrées sont exportées, avec des cellules numériques (tri, sommes). Aucune requête base
  supplémentaire : les deux axes viennent de la même réponse du serveur.
- Les périodes sans événement sont affichées à 0 : tableaux et courbes restent alignés.
- **Absences et retards** : mêmes règles et mêmes fonctions que les états (`AbsenceReportRules`,
  `RetardReportRules`) — un jour de congé, férié, week-end, OFF ou sans heures n'est jamais une
  absence ; un retard se mesure sur la première entrée du jour et une absence du matin l'annule.
  Aucun événement futur n'est compté (plafond à la veille). Le tableau de bord compte en revanche
  aussi les absences **déclarées** (`ABSENCE_AM` / `ABSENCE_PM`) dans ses jours d'absence, alors que
  l'état des absences ne montre que les journées planifiées non pointées.
- Regroupement et taux sont calculés par `PeriodGrouping` / `DashboardRules` (SoftTime.Domain) et
  couverts par des tests unitaires.

---

## 5 ter. Atterrissage de semaine des heures supplémentaires

Répond à une question : **qui finira la semaine en heures supplémentaires, et peut-on encore
l'éviter ?** Rien n'est stocké ni précalculé : chaque lecture repart des heures corrigées et du
planning, et suit donc immédiatement une correction ou un changement de planning.

- **Calcul** (`WeekLandingRules`, classe pure) : les jours déjà pointés sont pris tels quels ; les
  jours restants sont rejoués « conformément au planning » dans `OvertimeCalculator`. Congés,
  fériés, pause, 8 mars et mode de pointage suivent donc exactement les règles de la validation de
  semaine. S'y ajoute l'**écart habituel** du salarié à son planning : moyenne (réel − planifié)
  de ses 4 dernières semaines entièrement pointées (`Landing:DriftWeeks`), appliquée au prorata
  des heures encore planifiées. L'atterrissage ne passe jamais sous les heures déjà acquises.
- **Premier jour projeté** : aujourd'hui (la journée n'est pas finie), ou le lendemain du dernier
  pointage traité quand l'import a du retard — un jour pas encore importé n'est jamais compté
  comme une absence. L'écran affiche jusqu'où les pointages sont traités.
- **Seuils** : heures supplémentaires (> 0 h), passage à 150 % (> 8 h, `OvertimeRules.WeeklyHs130Cap`),
  plafond exonéré (> 20 h dans la semaine, `OvertimeRules.ExemptCeilingHours`). Pour chaque salarié
  au-dessus d'un seuil : **heures à retirer du planning restant** pour repasser dessous, ou
  « seuil déjà franchi » si les heures faites l'ont déjà dépassé.
- **Page `/predictions`** (États, permission `REPORTS:PREDICTIONS`) : fraîcheur des pointages,
  **horizon de 4 semaines** (`Landing:HorizonWeeks` ; « planning non saisi » quand rien n'est
  planifié ; un clic choisit la semaine), total à l'atterrissage et part déjà acquise, salariés en
  HS, passages à 150 %, heures encore évitables, alerte « jours sans planning », vue **par
  département** (un clic filtre la liste), détail **par salarié** (fait / contrat, HS acquises,
  reste planifié, atterrissage décomposé en planning + écart habituel, alerte et action), filtre
  « Alertes seulement », lien vers le planning des salariés.
- **API** : `GET /api/v1/predictions/landing?week=`, `GET /api/v1/predictions/landing/horizon`.
  Lecture seule. Le calcul reposant sur les pointages, la base SAGE **et** la pointeuse doivent
  être lisibles par le compte.
- **Moteur prédictif retiré** : l'ancien moteur ML.NET (modèles entraînés, base analytique `ml`,
  tâche de fond quotidienne) a été supprimé. Ses prévisions ignoraient les heures déjà pointées
  dans la semaine et n'étaient activées que si elles battaient une moyenne mobile de 4 semaines.
  Le schéma `ml` d'une base existante se supprime à la main avec
  `migrations/DropMlPredictionSchema.sql`.
- **Limites assumées** : un jour restant sans planning vaut 0 h (signalé) ; le plafond exonéré
  est comparé à la semaine seule, pas au cumul de la période de paie ; pas d'envoi d'alerte par
  courriel ; pas d'historique prévu / réel, puisque rien n'est stocké ; aucune sanction
  automatique, l'écran sert à ajuster le planning.
---

## 5 quater. Contrôle avant paie (`/closing`)

Page **en lecture seule** d'aide à la préparation de la paie : elle liste les anomalies de la période,
donne un score et un statut, et renvoie vers les écrans de correction existants. Accès : porte 6
(États) **et** permission `REPORTS:CLOSING` (l'administrateur y a toujours accès). Elle peut être coupée
par le réglage `Closing:Enabled`. Période par défaut : le mois civil complet précédent ; les jours en
cours et futurs ne sont jamais contrôlés (la période est plafonnée à la veille).

- **Types d'anomalies et gravité par défaut** (`PayrollClosingRules`, réglables par la section `Closing`) :

  | Type | Gravité | Ce que le contrôle signale |
  |---|---|---|
  | Pointage incomplet | Critique | Pointages qui ne s'apparient pas (mêmes règles que le traitement des pointages). |
  | Pointage manquant | Critique | Jour planifié avec heures, sans aucun pointage ni correction. |
  | Planning sans pointage | Importante | Une demi-journée non couverte (congé ou absence arbitrée d'un seul côté). |
  | Pointage sans planning | Importante | Jour pointé sans aucune ligne de planning. |
  | Doublon exact | Mineure en mono-pointage, Information en multi-pointage | Même matricule, même date, même heure. |
  | Quasi-doublon | Mineure en mono-pointage, Information en multi-pointage | Deux pointages séparés de moins de la tolérance (30 s par défaut). |
  | Férié non appliqué | Information | Jour férié dont la correction ne porte pas le drapeau férié (lien de correction seulement si un pointage existe ce jour-là). |
  | Corrections manuelles massives | Information | Salarié dont au moins 5 jours ET 50 % des jours contrôlés sont corrigés à la main (`Closing:CorrectionsManuelles*`). |
  | Heures supp. atypiques | Information | Semaine validée au-delà de 40 h supplémentaires (`Closing:HeuresSupHebdoSeuil`) ; semaine rattachée à la période de son dimanche. |

  Les quatre niveaux sont Information (poids 0), Mineure (1), Importante (3) et Critique (5). Une
  anomalie « Information » figure dans les comptages mais n'entre ni dans le score ni dans le statut.
- **Score** : `100 − 100 × (somme des poids) ÷ (poids maximum × jours-employé contrôlés)`, borné entre 0
  et 100 ; aucun score sans jour contrôlé.
- **Statut** : *Prêt* (aucune anomalie critique), *Bloquant* (critiques ÷ jours-employé contrôlés ≥ **5 %**),
  sinon *À corriger*. **Le seuil de 5 % est provisoire**, à confirmer par le manager (`Closing:CriticalRatioBlocking`).
  « Bloquant » est un statut d'affichage : aucun autre écran n'est empêché.
- **Filtres de la liste par type et par salarié** : une puce par type présent (`types=…`) et, depuis la carte
  « Salariés les plus touchés » (20 au plus, rangés par poids dans le score, `parSalarie` / `salariesConcernes`),
  « Voir ses anomalies » (`matricule=…`). Comme le filtre de gravité, ils ne changent que la liste.
- **Graphiques** : barres par gravité et par type ; avec la comparaison, carte « Évolution » (score avant → après,
  écarts par type en barres divergentes, composant partagé `soft-delta-chart`).
- **Tableau des départements** : « Analyser » relance l'analyse sur le département de la ligne.
- **Rapport imprimable** : « Imprimer le rapport » (A4, thème clair, service partagé `PrintReportService` avec
  l'export PDF du tableau de bord analytique) : en-tête daté, page affichée et liste complète.
- **Filtre et tri par gravité** : les quatre cartes de comptage servent de bascules de filtre de la liste,
  et un menu trie la liste du plus grave au moins grave (puis date, matricule, type). Seule la liste change :
  les comptages, le score et le statut restent ceux de l'analyse complète. Changer le filtre ou le tri relit
  la page 1 dans l'analyse en cache du serveur, sans nouvelle analyse (`gravites=…`, `tri=gravite`).
- **Export Excel** : le bouton « Exporter » produit un classeur (feuille « Synthèse » avec période, département,
  statut, score, comptages, filtres et tri appliqués ; feuille « Anomalies » avec toute la liste affichée, toutes
  les pages ; et, quand elles sont affichées, « Avancement », « Comparaison » et « Par département »). Il suit l'onglet, le filtre de gravité et le tri affichés, et refuse d'exporter si les données ont
  changé depuis l'analyse.
- **Filtre figé en session** : le filtre saisi, le dernier résultat (avec la date et l'heure de l'analyse), l'onglet,
  le filtre de gravité et le tri sont conservés en mémoire tant que la session dure. Aucune analyse ne se lance à
  l'ouverture de la page ni au retour : il faut cliquer sur « Analyser ». Un filtre modifié affiche « Filtre
  modifié, réanalyser ». L'état est vidé à la déconnexion et au changement de compte ou de base active.
- **Avancement de la paie** (`GET api/v1/closing/checklist`, séparé de l'analyse, même droit) : chargé avec
  l'analyse, une ligne par étape avec son état (*Fait*, *À faire*, *Information*, *Non déterminable*) et un
  lien vers l'écran concerné. Ces lignes n'entrent jamais dans le score ni dans le statut.

  | Étape | Source | Règle |
  |---|---|---|
  | Pointages importés | `T_POINTAGE` | Fait s'il existe des pointages sur la période pour le périmètre. |
  | Anomalies à arbitrer | `T_HEUREANOMALIE`, `T_HEURECORRIGER` | À faire s'il reste des anomalies, ou si des pointages existent sans aucune heure corrigée (traitement non lancé). |
  | Semaines validées | `T_HS_VALIDER`, `T_PLANNING_SAL` | Une semaine appartient à la période qui contient son dimanche ; fait si chaque semaine-salarié planifiée est validée. |
  | Calcul EXO/IMPO | `T_HSExoImp` | Fait pour la période exacte ; non déterminable si un calcul existe sur une autre période qui la recoupe. |
  | Heures imposables | `T_HSExoImp` | Information : salariés au-delà du plafond de 20 h exonérées (`OvertimeRules.ExemptCeilingHours`), seulement si le calcul existe. |
  | Synchronisation paie | journal d'audit (`SYNC_PAIE`) | Statut de la dernière synchronisation de la période ; non déterminable sans trace (le journal peut être désactivé). |

  Le cache de la checklist dure 60 s, par utilisateur, base, période et département.
- **Badge** : dans la barre du haut, près du bouton de thème, le nombre d'anomalies critiques du dernier résultat,
  pour les comptes ayant `REPORTS:CLOSING`, et seulement si une analyse existe en session. Il ne fait aucune requête
  et ne se rafraîchit pas seul ; il disparaît avec l'état de session.
- **Cache de l'analyse** : le serveur garde l'analyse en mémoire par utilisateur, base, période et département. Le délai de 120 s est **glissant** (chaque lecture du filtre, du tri, de l'onglet ou d'une page le renouvelle) sous un **plafond absolu de 600 s** depuis le calcul (`Closing:CacheMaxSeconds`, 0 à 3600) ; le délai glissant se règle par `Closing:CacheSeconds` (120 s par défaut, 0 à 3600, jamais au-delà du plafond ; 0 : aucun cache) ; « Analyser » recalcule toujours à neuf et repart d'un plafond neuf.
- **Âge de l'analyse** : près du résultat, « Analyse faite il y a X min », calculé sur l'heure conservée en session, sans requête.
- **Ce que l'écran ne fait pas** : aucune écriture, aucune correction saisie dans la page (seulement des liens vers
  les écrans existants), aucune règle métier de paie propre (mêmes anomalies, mêmes calculs d'heures supplémentaires,
  mêmes validations que les autres écrans), aucun blocage d'un autre écran, aucun envoi de message.

---

## 5 quinquies. Mon espace salarié (`/mon-espace`) — branche `feature/mon-espace`, désactivée par défaut

Écran **en lecture seule** où un salarié voit uniquement ses propres données : heures par semaine (semaines **validées**) avec
heures supplémentaires à 130 % / 150 % et cumuls EXO / IMPO, anomalies, absences et congés (lus dans SAGE), retards. Textes seuls, sans
export ni lien vers un autre écran. Fonction **non validée** : tout est additif et désactivable.

- **Activation** : variable d'environnement `SelfService__Enabled=true` (jamais dans `appsettings.json`). Par défaut la fonction est
  coupée : toutes les routes `api/v1/me/space/*` répondent 404.
- **Compte Salarié** : créé à la main par la RH dans l'écran Utilisateurs, avec le rôle « Salarié », le **matricule SAGE**, l'e-mail et **une
  seule base** cochée dans « Accès bases ». Le rôle se crée avec `migrations/mon_espace_role_salarie.sql` (fourni, jamais exécuté
  automatiquement, volontairement hors de `migrations/versions`, appliqué seul en développement) : une permission, `SELF:READ`, et
  aucune porte. Ne jamais ajouter de permission `SAGE:` ni `POINTEUSE:` à ce compte : elles donneraient la porte 1.
- **Garde** (`SelfServiceGuard`), appliquée à chaque requête et sans passer par la porte 1 : option activée ; permission `SELF:READ` effective ;
  matricule lu **uniquement** dans le jeton (aucun paramètre de requête, de route ni d'en-tête ne désigne un salarié) ; exactement
  **une** base autorisée dans `T_BDD_AUTORISE` ; matricule présent dans `T_CARDPAIE` de cette base. Sinon 403 avec un message explicite.
  Le client d'un salarié n'envoie **pas** `X-Sage-Database` : `TenantMiddleware` refuse tout en-tête de base à un compte sans porte 1.
- **Routes** (GET seulement, période `from`/`to` limitée à 4 mois dans les 4 derniers mois, sans jour futur) : `summary`, `weeks`, `anomalies`,
  `absences`, `delays`. Réponses sans identifiant de ligne, sans matricule ni nom.
- **Sources** : `T_HS_VALIDER`, `T_HSExoImp`, `T_HEUREANOMALIE` (lecture directe filtrée sur le matricule) ; règles pures `AbsenceReportRules`,
  `RetardReportRules`, `OvertimeRules.SplitWeekly`. Les congés SAGE se lisent pour tout le tenant (environ 3 à 5 s sur la démo, 13 s
  au plus mesuré) : seule la part du salarié est conservée, en cache à son nom (5 min). Aucun service de traitement, aucun état des retards
  (qui écrit `T_impr_RET`), aucun contrôle avant paie n'est utilisé.
- **Après la connexion** : un compte avec `SELF:READ` et aucune porte arrive sur `/mon-espace` ; tous les autres comptes sur `/dashboard`
  comme avant. L'entrée de menu « Mon espace » n'apparaît qu'avec `SELF:READ`.
- **Hors périmètre** : changement de mot de passe (aucun écran ni route « changer mon mot de passe » n'existe, ni changement obligatoire à la
  première connexion), création de comptes en lot, invitation, périmètre par département, solde de congés (absent de SoftTime).
- **Retrait** : mettre `SelfService__Enabled` à faux (ou le retirer) ; supprimer `SoftTime.Application/SelfService/`, `SelfSpaceController.cs`,
  `features/self-space/` et les ajouts à `app.routes.ts`, `login.ts`, `shell.ts`, `DependencyInjection.cs` et `PermissionCatalog.cs`. Reste en
  base : le rôle « Salarié », ses lignes `T_ROLE_PERMISSION`, les comptes créés et leurs accès bases ; `migrations/mon_espace_retrait.sql`
  les liste, puis les supprime après relecture.

**Corrections de revue (complètent et, en cas de désaccord, remplacent les points ci-dessus)** :

- **Garde — le compte est relu à chaque appel.** Le matricule du jeton n'est plus la seule source : la garde relit le compte dans
  `T_RESPONSABLE` (une requête, sans cache). Compte supprimé, matricule retiré ou différent de celui du jeton (réaffectation par la RH,
  jeton de 12 h encore valide) : 403 explicite « reconnectez-vous ». `T_RESPONSABLE` n'a pas de colonne « actif » : un compte désactivé est un
  compte supprimé ou sans `SELF:READ`. Seules les routes `api/v1/me/space/*` font cette lecture ; aucune autre route n'est touchée.
- **Matricule partagé = refus.** Si un autre compte porte la même clé de matricule (casse, espaces et zéros de tête ignorés), **aucun** des
  porteurs ne lit Mon espace (403 « matricule porté par plusieurs comptes : contactez la RH »), sans citer l'autre compte. À la création des
  comptes Salarié, donner un matricule unique.
- **Aucune conservation par le navigateur.** Toutes les réponses de `api/v1/me/space/*` (succès, 400, 403, 404 de la fonction désactivée)
  portent `Cache-Control: no-store` et `Pragma: no-cache`. Seuls les 401 (jeton absent, rendus avant le contrôleur) et les 405 n'en portent pas :
  ils ne contiennent aucune donnée.
- **Congés SAGE : une seule lecture partagée.** La lecture du tenant (qui prend plusieurs secondes) n'est lancée qu'une fois à la fois par
  base, quel que soit le nombre de salariés qui la demandent ; les autres attendent la même lecture. Elle est conservée **5 minutes** en
  mémoire, dans le cache seulement : aucune route ne la rend, chaque salarié n'en reçoit qu'une copie de sa propre part (clé utilisateur |
  base | matricule, jamais plus vieille que la lecture). Un échec n'est jamais conservé. Plafond mémoire : 8 bases et 512 parts de salarié ;
  mesuré environ 1,3 Mo pour la démo (3 008 cartes).
- **Lectures filtrées dans la requête.** Plannings, anomalies et catégories du salarié sont filtrés dans la requête SQL (et recontrôlés par la
  clé canonique) au lieu d'être lus pour tout le tenant puis filtrés.
- **Erreurs affichées durablement (écran).** Une erreur de `api/v1/me/space/*` s'affiche dans un encadré au-dessus des cartes, en texte seul (jamais le
  JSON brut), avec le message du serveur et une consigne : « Contactez la RH. » (403 de compte), « Reconnectez-vous. » (message demandant de se
  reconnecter), « Choisissez un autre mois. » (400), « Réessayez dans un instant. » (erreur serveur ou réseau). Les erreurs de compte, de période ou de
  session remplacent les quatre cartes ; avec l'option coupée (404 « Mon espace n'est pas activé »), l'encadré indique « Mon espace n'est pas encore activé :
  contactez la RH. ». Le menu et la redirection après connexion ne changent pas (ils dépendent de `SELF:READ`, pas de l'option serveur).
- **Semaine à cheval.** Une semaine qui chevauche deux mois apparaît dans chacun (le filtre retient les semaines dont le lundi est dans la période élargie au lundi
  précédent) : une note sous la carte « Mes heures par semaine » le rappelle, les totaux de deux mois ne doivent donc pas être additionnés.

---

## 5 sexies. Variables envoyées à la paie (écran Bases SAGE) — branche `feature/config-paie`

Chaque base SAGE (le « tenant » : il n'existe pas d'entité Entreprise) choisit, par case à cocher, quelles variables de la page Heures
supplémentaires sont **envoyées** à la paie. **Le calcul ne change pas** : les neuf variables (EXO 130, EXO 150, IMPO 130, IMPO 150, Férié,
Nuit, Dimanche, Retard, Absence) restent calculées et affichées ; seul l'envoi est filtré.

- **Valeurs par défaut = comportement d'origine** : les sept variables d'heures supplémentaires cochées, Retard et Absence décochées. Une base
  sans configuration (aucune ligne dans `T_PAIE_VARIABLE`) se comporte exactement comme avant (mêmes commandes SQL, mêmes valeurs, même ordre).
- **Variable décochée = non écrite** : ni validée, ni écrite, ni mise à 0, ni comptée comme manquante : la valeur déjà présente en paie
  (`T_CUMSAL` en SAGE, colonne de la table d'écriture en autre base) n'est pas touchée. En autre base, une catégorie décochée n'a pas besoin de
  ligne dans `T_PAIE_ECRITURE` ; l'écriture reste transactionnelle avec son préflight. Si aucune variable d'heures supplémentaires n'est cochée,
  rien n'est écrit.
- **Remise à 0 des salariés sans valeur (C2b)** : l'envoi écrit **0** pour chaque variable **cochée** des salariés du périmètre qui n'ont plus de
  ligne de valeurs sur la période (exemple : 9 h de Nuit envoyées, puis la Nuit retombe à 0 après correction de pointage : le renvoi remet 0 dans la
  paie au lieu de laisser 9 h). Le **périmètre** est celui de la requête de synchronisation : cartes de la base, filtrées par département et plage de
  matricules ; un salarié hors périmètre n'est jamais touché. Les salariés avec valeurs sont écrits comme avant ; l'envoi est idempotent.
  STANDARD : sur les lignes `T_CUMSAL` existantes seulement (le compteur « mise(s) à jour sans ligne » compte aussi les cumuls absents des salariés
  remis à 0) ; un salarié absent de SAGE n'est pas touché. Autre base : mêmes colonnes, même préflight, même transaction ; un salarié sans valeur
  **absent de la table de paie** (ou présent plusieurs fois) est ignoré sans erreur et signalé dans le message : aucune ligne n'est créée.
  Le message de fin et le résultat distinguent les salariés écrits des salariés remis à 0 ; la synthèse du journal `SYNC_PAIE` et la ligne
  `T_PAIE_ENVOI` comptent des **catégories** mises à jour (valeurs et zéros ensemble). La page Heures supplémentaires l'annonce dans la
  confirmation de l'envoi (**tant que l'option de la base est cochée**, voir la section « 5 octies »).
  **Refus d'une sélection sans heure calculée** : avant toute écriture, si la sélection de l'envoi (période exacte, base, département, plage de
  matricules) n'a **aucune ligne `T_HSExoImp`**, la synchronisation est refusée (400, STANDARD comme autre base) : « Aucune heure calculée sur cette période pour
  cette sélection : lancez le calcul avant d'envoyer à la paie. ». Rien n'est écrit (ni valeur, ni zéro), aucun marqueur `T_PAIE_ENVOI`, aucune synthèse de
  succès au journal. Le message du serveur est affiché tel quel par la page Heures supplémentaires. Un département ou une plage de matricules sans ligne est refusé même si
  le reste de la base en a. Avec au moins une ligne dans la sélection, la remise à 0 des autres salariés du périmètre s'applique comme décrit ci-dessus.
  **Avertissement** : en STANDARD, si le numéro de salarié SAGE d'un salarié sans valeur est introuvable (lecture des salariés SAGE impossible, liste
  vide, salarié absent), l'envoi des salariés avec valeurs n'est pas bloqué, mais le message de fin dit « N salarié(s) sans valeur n'ont PAS été remis à 0 ...
  leurs anciens cumuls restent dans SAGE » et la synthèse du journal `SYNC_PAIE` porte `AVERTISSEMENT SalariésNonRemisÀ0=N` (et `SalariésRemisÀ0=N`).
  **Hypothèse à confirmer par le RH** : personne ne saisit à la main ces mêmes codes (HS01 à HS07, code de Retard) dans SAGE. Sinon, le 0 écraserait
  cette saisie pour les salariés sans valeur. **Réglable depuis C2c** : l'option « Remettre à 0 les salariés sans valeur à l'envoi » (cochée par
  défaut) se décoche par base, sans attendre la réponse du RH (section « 5 octies »).
- **Écran** : carte « Variables envoyées à la paie » sous la liste des bases SAGE, pour la **base active**. Base SAGE : codes HS01 à HS07 en lecture
  seule (écran Codes constantes), code de paie et unité (heures ou jours) modifiables pour Retard et Absence. Autre base : colonnes de
  `T_PAIE_ECRITURE` en lecture seule pour les heures supplémentaires, colonne cible (liste lue dans la table d'écriture configurée) et unité pour
  Retard et Absence. Rien n'est envoyé au serveur avant la confirmation ; les erreurs restent affichées dans la carte.
- **Droits** : les mêmes que la modification de la base dans l'écran Bases de données (porte Bases + `SAGE:{base}:UPDATE`, ou administrateur),
  vérifiés par le serveur (`GET`/`PUT api/v1/payroll-variables`). `GET api/v1/payroll-variables/actives` (noms seulement) sert aux portes
  Traitements, États et Bases. Aucun droit nouveau.
- **Retard et Absence** : visibles, mais impossibles à cocher tant que leur code de paie (SAGE) ou leur colonne cible (autre base) et leur unité ne
  sont pas renseignés. **Leur envoi réel n'existe pas encore (lot C)** : même cochées, elles ne sont jamais écrites.
- **Mois déjà envoyé** : une modification est refusée (409) si une période déjà envoyée en paie recouvre le mois civil en cours de la base
  active ; sinon elle s'applique au prochain envoi. Le marqueur est la table `T_PAIE_ENVOI` (base, période, date, auteur, statut, lignes), écrite
  par les writers de paie indépendamment du journal central : en STANDARD dès la première ligne `T_CUMSAL` modifiée (un échec partiel compte
  comme envoyé) ; en autre base juste avant la validation de la transaction de paie (si le marqueur ne peut pas être écrit, la paie est annulée ;
  si la validation échoue ensuite, le marqueur reste au statut `INCERTAIN` et compte comme envoyé). Aucun marqueur si rien n'est écrit.
  **Limite** : les envois faits avant la migration 007 ne sont pas repris (aucune reprise de l'historique) : un mois envoyé avant la mise en
  service n'est pas détecté.
- **Journal** : toute modification de la configuration passe par le journal central (qui, quand, avant, après, table `T_PAIE_VARIABLE`). Le
  journal `SYNC_PAIE` des synchronisations est inchangé.
- **Page Heures supplémentaires** : les colonnes des variables non envoyées sont grisées, avec la mention « non envoyée à la paie » dans
  l'en-tête ; elles ne sont jamais masquées (par défaut : Retard et Absence).
- **Contrôle avant paie** : aucune anomalie ni ligne de checklist pour une variable décochée (« férié non appliqué » si Férié est décochée ;
  « heures supplémentaires atypiques » si les quatre variables EXO/IMPO le sont ; ligne « Heures imposables » si IMPO 130 et IMPO 150 le sont).
  Les caches de l'analyse et de la checklist sont vidés à chaque enregistrement.
- **STANDARD, cumul absent** : une mise à jour `T_CUMSAL` qui ne modifie aucune ligne (cumul absent dans SAGE pour le salarié) est maintenant
  signalée dans le message de fin de synchronisation ; les cas valides gardent le message d'origine.
- **Base de données** : migration versionnée `migrations/versions/007_variables_paie.sql` (rejouable, aucune suppression), appliquée par
  `Database:AutoMigrate` ; sans elle, l'API refuse de démarrer avec un message clair.

## 5 septies. Envoi de Retard à la paie (lots C1 et C2) — branche `feature/envoi-variables-paie`

Complète la section précédente : Retard devient envoyable. (Absence : envoyable depuis le lot C3, section 5 nonies.)

- **Liste unique des variables (C1)** : un seul catalogue des neuf variables (identifiant, libellé, unités, valeur lue dans la ligne
  `T_HSExoImp`, destination désignée par un code commun ou propre à la base) ; rien n'y est propre à SAGE. Les écritures de paie, la validation
  et la configuration en dérivent. Sortie inchangée : une base sans configuration, ou avec Retard et Absence décochées, produit les mêmes
  écritures (mêmes SQL, mêmes valeurs, même ordre, mêmes messages) qu'avant.
- **Retard en base SAGE** : unité heures ; le code de paie se choisit dans une liste lue dans `T_CST` de la base active. L'enregistrement refuse
  un code absent de `T_CST` (et refuse si `T_CST` ne peut pas être lu). Retard est écrit dans `T_CUMSAL` par la même commande que les heures
  supplémentaires, après les sept, avec la valeur `T_HSExoImp.RETARD` affichée dans la page Heures supplémentaires.
- **Retard en autre base** : section « Destinations dans la base de paie » de la carte : table, colonne matricule et colonne de chaque variable
  envoyable, choisies dans des listes lues dans la base de paie (`INFORMATION_SCHEMA`). Le serveur vérifie que la table et les colonnes
  existent, que les colonnes sont numériques et, pour Retard, qu'elles acceptent deux décimales. Les destinations sont enregistrées dans
  `T_PAIE_ECRITURE` (catégorie `Retard`). Endpoints `GET`/`PUT api/v1/payroll-variables/destinations` et
  `GET api/v1/payroll-variables/destinations/colonnes?table=` : mêmes droits que la configuration des variables, refus si le mois est envoyé,
  journal central, caches du Contrôle avant paie vidés. Aucune migration (la colonne `CATEGORIE` accepte `Retard`).
- **Absence (état à la fin de C2)** : case désactivée avec « Envoi de l'absence : prochaine étape » ; une Absence cochée était refusée à
  l'enregistrement. Remplacé par le lot C3 (section 5 nonies).
- **Défaut connu (corrigé en C2b)** : un salarié sans ligne `T_HSExoImp` sur la période n'était pas écrit ; son cumul en paie (Retard compris)
  gardait la valeur d'un envoi précédent. Il est désormais remis à 0 (variables cochées), sauf si l'option de la base (C2c) est décochée.

## 5 octies. Option par base « Remettre à 0 les salariés sans valeur à l'envoi » (lot C2c) — branche `feature/envoi-variables-paie`

Le RH règle lui-même, **par base**, ce que l'envoi fait des salariés du périmètre qui n'ont plus de valeur (comportement de C2b, section
précédente). L'hypothèse « personne ne saisit à la main ces codes dans SAGE » n'étant pas confirmée, l'option évite d'écraser une saisie manuelle.

- **Valeur et défaut** : option **cochée par défaut**. Une base **sans ligne de configuration** vaut « cochée » : aucun changement de comportement
  pour une base existante (comportement de C2b : zéros pour les variables cochées).
- **Option cochée** : comportement de C2b inchangé (lignes synthétiques à 0, message « N salarié(s) sans valeur ont été remis à 0 », avertissement
  « n'ont PAS été remis à 0 » si leur numéro SAGE est introuvable).
- **Option décochée** : l'envoi n'ajoute aucune ligne synthétique. Aucun salarié n'est remis à 0 (`EmployeesReset = 0`) ; les salariés avec valeurs
  sont écrits comme avant (mêmes variables cochées, mêmes valeurs) ; ni la phrase « remis à 0 » ni l'avertissement « non remis à 0 » n'apparaissent
  dans le message, le résultat ou la synthèse du journal `SYNC_PAIE` (rien n'était demandé). STANDARD et autre base.
- **Inchangé dans les deux cas** : le calcul des heures, le filtre à l'envoi (variable cochée), le **refus d'une période sans aucune ligne calculée**,
  le verrou du mois, le marqueur `T_PAIE_ENVOI`, le journal `SYNC_PAIE`, les droits et le statut `INCERTAIN` d'une autre base.
- **Écran** : sur la carte « Variables envoyées à la paie », sous le tableau, un interrupteur (style des autres interrupteurs de la carte) libellé
  « Remettre à 0 les salariés sans valeur à l'envoi », avec l'aide « Évite qu'un ancien cumul reste dans la paie quand un salarié n'a plus d'heures. À
  décocher si quelqu'un saisit ces codes à la main dans la paie. ». Mêmes états que la carte : brouillon et « Annuler les changements », désactivé pendant
  l'enregistrement et quand le mois est envoyé (« Mois déjà envoyé : modification impossible. »), absent sans permission. Il part **dans le même
  enregistrement que les variables** (une seule confirmation) ; la confirmation cite « Remise à 0 des salariés sans valeur : activée / désactivée ».
  Compatible largeur étroite (le tableau passe en cartes sous 768 px, l'option reste sous les cartes) et thème sombre.
- **Confirmation de « Synchroniser vers SAGE »** (page Heures supplémentaires) : option cochée, la question annonce la remise à 0 (texte de C2b) ;
  option décochée, elle se limite à « Pousser les HS vers SAGE (T_CUMSAL) ? » (ou « Écrire les HS dans la base X ? »). L'option est relue au moment de
  confirmer.
- **API** : `GET api/v1/payroll-variables` renvoie `remiseAZeroSansValeur` (vrai sans ligne) ; `GET …/actives` aussi. `PUT api/v1/payroll-variables`
  accepte l'**ancien corps** (tableau de variables : option inchangée) ou l'objet `{ "variables": [...], "remiseAZeroSansValeur": true|false }`
  (champ absent ou `null` : option inchangée). Mêmes contrôles que les variables : droit (porte Bases + `SAGE:{base}:UPDATE`, ou administrateur), refus
  409 si une période envoyée recoupe le mois en cours, caches de la sélection et du Contrôle avant paie vidés, atomicité avec les variables (une
  validation qui échoue n'écrit rien).
- **Stockage** : table générique **`T_PAIE_OPTION`** (`BDD_SAGE`, `CLE`, `VALEUR`, `MODIFIE_LE`, `MODIFIE_PAR`, index unique sur base et clé), clé
  **`RemiseAZeroSansValeur`**, valeur « 1 » ou « 0 ». Une table générique plutôt qu'une colonne de `T_PAIE_VARIABLE` : l'option n'appartient à aucune
  variable, et le lot C3 y ajoutera d'autres réglages (heures par jour, liste de codes d'absence) sans nouvelle migration. Rien n'est inséré par la
  migration : l'absence de ligne vaut la valeur par défaut ; cocher à nouveau met la ligne à jour (jamais de suppression). Journal central avec l'auteur
  (comme `T_PAIE_VARIABLE`).
- **Migration 008** : `migrations/versions/008_options_paie.sql` (additive, rejouable : `T_PAIE_OPTION` et son index ne sont créés que s'ils n'existent pas,
  aucune donnée insérée), appliquée par `Database:AutoMigrate`. Sans elle, l'API refuse de démarrer avec « Les options de paie sont absentes
  (T_PAIE_OPTION) : activez Database:AutoMigrate (développement) ou exécutez migrations/versions/008_options_paie.sql. » (contrôle comparable à celui de
  007). **Production sans AutoMigrate** : exécuter une fois ce script sur la base SoftTime, avant de déployer l'API :
  `sqlcmd -S <serveur> -d <base SoftTime> -E -b -i 008_options_paie.sql` (rejouable sans risque). Les futurs lots de clôture prendront 009 et suivants.

## 5 nonies. Envoi de l'Absence à la paie (lot C3) — branche `feature/envoi-variables-paie`

L'Absence (non justifiée) devient envoyable à la paie comme Retard, en **heures** ou en **jours**, avec deux réglages par base : le nombre d'heures
d'une journée et les codes d'absence comptés comme non justifiés. Le filtre à l'envoi (variable cochée), la remise à 0 (option C2c), le refus d'une
période sans ligne calculée, le verrou de mois, le journal, les droits et le statut `INCERTAIN` d'une autre base sont inchangés.

- **Envoi** : Absence **décochée par défaut** (une base existante envoie exactement ce qu'elle envoyait). Cochée, elle exige un code de paie (base
  SAGE : liste lue dans `T_CST`, code inconnu refusé avec « Le code de paie XX99 de la variable Absence n'existe pas dans la base SAGE … ») ou une
  colonne (autre base : section « Destinations », colonne numérique avec décimales, catégorie `Absence` de `T_PAIE_ECRITURE`), et une unité.
  Décochée, elle n'est jamais écrite ni résolue, même avec un code enregistré (le code, l'unité et les heures par jour sont conservés).
  L'Absence est écrite après les sept variables d'heures supplémentaires et Retard ; elle compte dans la synthèse du journal `SYNC_PAIE`.
- **Unités** : « heures » = la valeur affichée dans la page Heures supplémentaires. « Jours » = **heures ÷ heures par jour, arrondi au centième, la
  moitié vers le haut** (24 h à 8 h par jour : 3 ; 25,5 h : 3,19 ; 24 h à 6 h par jour : 4 ; 1 h : 0,13). Retard reste en heures. En autre base, une
  colonne à une décimale refuse une valeur en jours à deux décimales (« La valeur … du matricule … n'est pas compatible avec la colonne … »), comme
  Retard.
- **Heures par jour** : défaut **8**, entre **1 et 24**, **une décimale au plus** (« Le nombre d'heures par jour de l'absence doit être compris
  entre 1 et 24, avec une décimale au plus (valeur reçue : … ). »). Champ affiché sur la carte quand l'unité est « jours ». Valeur 8 : aucune ligne
  stockée (clé `AbsenceHeuresParJour` créée seulement pour une autre valeur).
- **Codes d'absence comptés comme non justifiés** : liste des codes de `T_CODEABSENCE` (libellé), cochés ou non, plus « Arbitrage d'absence sans
  code ». **Défaut** (aucun réglage enregistré) : `ABSENCE NON JUSTIFIEE` et l'arbitrage sans code, soit le comportement d'avant C3 pour une
  absence arbitrée sans code. Un code marqué « non payé » (`NOT_PAY`) n'est jamais coché d'office : il est seulement signalé « non payé » et
  « à envisager ». Comparaison tolérante sur le libellé (casse, accents, espaces en trop). Un code enregistré qui n'existe plus dans
  `T_CODEABSENCE` reste affiché avec « code introuvable » et reste compté tant qu'il n'est pas décoché ; un code renommé n'est plus reconnu tant
  qu'il n'est pas coché sous son nouveau libellé.
- **Règle de calcul** : un jour planifié non chômé, hors congé et hors férié, **sans aucune ligne** est toujours compté. Une journée sans présence
  couverte par des absences arbitrées (`ABSENCE_AM` / `ABSENCE_PM` sans heure) n'est comptée que si **au moins une** de ces lignes porte un code
  compté (ou n'a pas de code et que l'arbitrage sans code est compté) ; sinon elle est « justifiée » et n'entre pas dans l'Absence. Demi-journées
  inchangées : une demi-journée arbitrée sans heure compte pour la journée entière ; une présence sur l'autre moitié donne 0.
- **Limite à connaître** : la règle ne s'applique qu'au **prochain « Calculer »**. Les valeurs déjà stockées dans `T_HS_VALIDER` / `T_HSExoImp` ne
  changent pas quand le RH modifie les codes comptés : il faut **recalculer les périodes déjà calculées avant de les envoyer**. La confirmation
  d'enregistrement et l'aide de la carte le rappellent.
- **Écran** : sur la carte « Variables envoyées à la paie », ligne Absence comme Retard (liste des codes de paie **avec recherche en tapant**,
  unité heures ou jours, champ « Heures par jour » en jours) et, sous elle, le groupe « Codes d'absence comptés comme non justifiés ». Brouillon,
  « Annuler les changements », verrou de mois, absence de permission et largeur étroite comme le reste de la carte. La confirmation d'enregistrement
  cite les changements (« Absence : 6 heures par jour », « Codes d'absence comptés ajoutés : … », « Arbitrage d'absence sans code : non compté »).
  La confirmation de « Synchroniser vers SAGE » précise, si l'Absence est cochée en jours : « L'absence est envoyée en jours (heures ÷ 8 h par jour,
  arrondi au centième). »
- **Stockage** (table `T_PAIE_OPTION` du lot C2c, **aucune migration**) : `AbsenceHeuresParJour` ; `AbsenceSansCodeCompte` (« 1 » ou « 0 »,
  marqueur « un réglage est enregistré » : sans lui, c'est le défaut) ; une ligne **`AbsenceCodeCompte.N`** par code compté, `VALEUR` = libellé
  de `T_CODEABSENCE`. L'unité de l'Absence est dans `T_PAIE_VARIABLE.UNITE` (`H` ou `J`). Journal central avec l'auteur.
- **API** : `GET api/v1/payroll-variables` renvoie `heuresParJour` et `absenceCodes` (`parDefaut`, `sansCode`, `codes[]` : `libelle`, `compte`,
  `introuvable`, `nonPaye`, `suggere`) ; `GET …/actives` renvoie `uniteAbsence` et `heuresParJour`. `PUT` accepte en plus `heuresParJour` et
  `absenceCodes` (`{ sansCode, codes[] }`), champs absents ou `null` : inchangés ; un seul appel, une seule sauvegarde, atomique (une validation
  qui échoue n'écrit rien), mêmes droit, verrou de mois (409), journal et caches vidés.
- **Lecteurs de l'Absence** : `WeeklyValidationService`, `OvertimeExoImpoService` (Calculer) et `OvertimeDetailService` passent par le **même
  chargement du réglage** (`OvertimeWeekContext`) : le calcul, la validation de la semaine et le détail d'un salarié donnent le même résultat. La
  **clôture** et le **tableau de bord** ont leurs propres règles (jours planifiés sans pointage, absences déclarées) et ne lisent pas ce réglage.
  L'**export**, les **rapports** (état des absences) et **Mon espace** lisent les valeurs stockées ou leurs propres règles : ils ne changent qu'après
  un nouveau calcul.
- **Effet du réglage par défaut** : après un recalcul, l'Absence affichée n'inclut plus une journée dont les absences arbitrées portent un code
  autre que `ABSENCE NON JUSTIFIEE` (par exemple un congé exceptionnel arbitré) ; les jours sans ligne et les arbitrages sans code sont inchangés.
  Le RH peut cocher d'autres codes dans la carte pour les compter.

---

## 5 decies. Clôture de paie (lot D) — branche `feature/cloture-paie-lot-d`

La clôture **fige une période déjà envoyée à la paie**, définitivement. Deux actions distinctes : **envoyer** (rejouable), puis **clôturer**
(irréversible). Une clôture par (base, début, fin), sur la **base entière**. Tant que rien n'est clôturé, aucun comportement existant ne change.

- **Principe** : chaque envoi réussi fige un **instantané** des valeurs réellement écrites ; la clôture choisit le dernier envoi de la période,
  vérifie son empreinte et le Contrôle avant paie, puis interdit toute modification des données de la période.
- **Instantané (D1)** : `T_PAIE_INSTANTANE` (entête : base, période, type de base, date et auteur de l'envoi, filtre de l'envoi, option de remise
  à 0, heures par jour, version du format, empreinte, nombre de lignes, mises à jour sans ligne, salariés non remis à 0) et
  `T_PAIE_INSTANTANE_LIGNE` (matricule, numéro de salarié SAGE, variable, destination = code de constante SAGE ou `schéma.table.colonne`, unité
  `H`/`J`, valeur, indicateur « zéro de remise à 0 »). Contenu : **exactement les valeurs écrites** (zéros de remise à 0 compris, Absence dans
  l'unité choisie) ; jamais les variables décochées ni les salariés hors périmètre ; une mise à jour qui n'a modifié **aucune ligne** (cumul absent
  dans SAGE) n'est pas figée, elle est comptée dans l'entête. Un renvoi crée un nouvel instantané et ne remplace rien.
- **Transactions** : STANDARD (SAGE, écritures non transactionnelles) : statut `SUCCES` et instantané écrits **dans une seule transaction
  SoftTime** à la fin de l'envoi ; si elle échoue, l'envoi passe à `INCERTAIN` et le message de fin le signale (renvoyer la période, les valeurs SAGE
  étant réécrites à l'identique). AUTRE : instantané écrit dans la **même transaction que le marqueur**, avant la validation de la paie ; s'il échoue,
  la paie est annulée ; si la validation de la paie échoue, l'envoi est `INCERTAIN`. Un envoi en échec n'a pas d'instantané.
- **Empreinte (format 1)** : SHA-256 du texte UTF-8 sans BOM, lignes séparées par `\n` (aucune en fin) : première ligne
  `SOFTTIME-PAIE-INSTANTANE|1|{base}|{début aaaa-MM-jj}|{fin aaaa-MM-jj}|{TYPE}`, puis une ligne par valeur
  `{matricule}|{numéro de salarié ou vide}|{variable}|{destination}|{unité}|{valeur}|{0 ou 1}` (valeur en culture invariante, point décimal,
  exactement 4 décimales, arrondi au plus proche, milieu loin de zéro), triées en ordinal sur matricule, variable, destination, numéro de salarié ;
  résultat en 64 caractères hexadécimaux minuscules. Ni la date ni l'auteur n'y entrent : deux envois identiques ont la même empreinte. Tout
  changement de format exige une nouvelle version.
- **Immuabilité** : aucun chemin de modification ni de suppression dans l'application ; déclencheurs `INSTEAD OF UPDATE, DELETE` sur les tables
  d'instantané (erreur 51991) et de clôture (51992) ; clés étrangères vers `T_PAIE_ENVOI` (un envoi figé ne peut plus être supprimé). Un
  administrateur de la base qui désactiverait les déclencheurs serait détecté par la vérification d'empreinte.
- **Lecture (D1)** : `GET api/v1/payroll-snapshots?from&to` (envois de la période), `/{envoiId}` (détail), `/{envoiId}/verification` (empreinte
  recalculée, « Instantané ALTÉRÉ » sinon). Mêmes droits que la lecture des variables (porte Bases + `SAGE:{base}:UPDATE`, ou administrateur).
- **Clôturer (D2)** : `POST api/v1/payroll-closures { from, to }`, droit **`SAGE:{base}:CLOTURE`** (porte Bases), **jamais accordé par défaut**
  (ni rôle par défaut, ni « Tout cocher ») : case « Clôturer la paie » de la base dans l'écran des droits ; l'administrateur est toujours autorisé.
  Refus 409, dans cet ordre : période non terminée (fin ≥ aujourd'hui) ; déjà clôturée ou recoupant une clôture ; aucun envoi de la période exacte ;
  dernier envoi `INCERTAIN`, en échec ou inachevé ; sans instantané ; **filtré** (« Lancez un envoi sans filtre avant de clôturer ») ; empreinte
  altérée ; heures recalculées depuis l'envoi (valeurs figées ≠ `T_HSExoImp` actuel) ; Contrôle avant paie désactivé ; Contrôle **sans département,
  sans cache** autre que « Prêt » (« À corriger » et « Bloquant » refusés, message avec le nombre d'anomalies critiques, leurs types et les dix
  premières). Succès : ligne `T_PAIE_CLOTURE` (date, auteur, envoi et instantané, empreinte, statut et résumé du Contrôle), entrée `CLOTURE_PAIE` du
  journal central avec l'auteur (filtrable par action dans l'onglet Journal), caches de l'analyse, de la checklist et des variables vidés.
  Lectures : `GET …/periods` (périodes envoyées et état), `GET …/state?from&to` (bandeau des pages).
- **Aucun déblocage** : ni bouton, ni route (PUT, PATCH, DELETE → 405), ni service. **Correction d'un mois clos** : sur la **période suivante**, par
  une ligne de régularisation (valeur négative ou complémentaire) avec motif écrit — procédure manuelle à ce jour (saisie de la régularisation dans
  la paie ou sur la période suivante), la fonction dédiée n'existe pas encore. **Dernier recours** (erreur grave) : restauration d'une sauvegarde de
  la base SoftTime antérieure à la clôture, décidée par la direction, consignée hors application ; elle annule aussi tout ce qui a été saisi depuis.
- **Nouvel envoi** : refusé (409) si la période recoupe une période clôturée : « Période clôturée (du … au …) : nouvel envoi en paie impossible. Les
  corrections se font sur la période suivante. » Un envoi commencé avant la clôture ne peut plus figer son instantané (STANDARD : `INCERTAIN`).
- **Verrous sur les sources (D3)** : contrôle commun (`PayrollClosureGuard`), refus 409 « Période clôturée (du … au …) : modification impossible. Les
  corrections se font sur la période suivante. », **rien n'est écrit** : import de pointages (pointeuse : plage ; Excel : une ligne dans une période
  close refuse **tout le fichier**, y compris les pointages tardifs), pré-traitement, modification et purge de pointages (la purge porte jusqu'au
  lendemain de la fin), anomalies (arbitrage, conversion en lot, suppression), heures corrigées (saisie, modification, suppression, import en lot),
  planning (saisie, déplacement d'une journée, suppression ; imports et générations en lot), validation, annulation et purge de semaine (l'aperçu
  d'une semaine close n'écrit plus rien), calcul et purge des heures supplémentaires, **cantine et frais** (calcul, purge, envoi vers `T_CUMSAL`).
  En plus, un **filet commun à l'enregistrement** refuse tout ajout, modification ou suppression d'une ligne datée de ces tables dans une période
  close (dates avant et après modification), quel que soit le chemin. Une période voisine et une autre base restent modifiables.
- **Restent modifiables** (paramètres généraux, non datés ou globaux) : tolérances, catégories, affectations, majorations, jours fériés, codes
  d'absence, shifts, mode de pointage, cartes salariés, configuration des variables (soumise à son **verrou de mois**, distinct et inchangé). Leur
  changement n'a **aucun effet** sur une période close : son recalcul et son renvoi sont refusés, et la paie garde l'instantané.
- **Articulation avec le verrou de mois** : le verrou de mois (lot B) empêche de changer la configuration des variables tant qu'une période envoyée
  recoupe le mois civil en cours ; la clôture fige une période précise, sans limite de durée. Ils sont indépendants : la clôture n'a pas besoin du
  verrou de mois (un envoi clos ne peut plus être refait, quelle que soit la configuration), et le verrou de mois reste la règle de la configuration.
- **Limites** : les **congés SAGE** sont lus en direct dans SAGE et **ne peuvent pas être verrouillés** par SoftTime ; un congé modifié après la
  clôture ne change rien à la paie figée (instantané) mais peut faire diverger l'affichage recalculé (rapports, Mon espace) : à régulariser sur la
  période suivante. Les périodes envoyées avant la migration 009 n'ont pas d'instantané : il faut les renvoyer pour pouvoir les clôturer.
- **Écran (D4)** : écran **Bases SAGE**, carte « Clôture de paie » sous « Variables envoyées à la paie » (mêmes droits de base que l'envoi) :
  périodes envoyées, état (Envoyée, Clôturée avec date et auteur), dernier envoi et filtre, empreinte raccourcie et copiable, bouton « Clôturer »
  (droit de clôture) avec confirmation rappelant la période, l'absence de retour en arrière et la correction sur la période suivante ; refus du
  serveur affiché tel quel ; vue en cartes sous 768 px. Bandeau « Période clôturée » et boutons de modification désactivés (avec explication) sur
  Heures supplémentaires, Heures corrigées, Pointages, Anomalies et Validation semaine quand la sélection recoupe une période close. Le titre du
  bandeau dit « Période clôturée » ; son texte, en une phrase, ne le répète pas : « Du jj/mm/aaaa au jj/mm/aaaa, aucune modification n'est
  possible : les corrections se font sur la période suivante. »
- **Cantine et frais de déplacement (D5)** : ils sont aussi envoyés à la paie (cumuls `T_CUMSAL` des constantes `CANTINE_CODE` et `HA06`, base
  **STANDARD** seulement ; une base AUTRE est refusée comme avant) et ont désormais la **même preuve** que les heures.
  - **Domaines** : chaque envoi et chaque instantané portent un domaine, `HEURES` (seul domaine avant D5, valeur par défaut), `CANTINE` ou
    `FRAIS`. Les périodes de la carte de clôture sont celles des envois d'**heures** ; le verrou de mois de la configuration ne compte que les envois
    d'heures.
  - **Envoi** : marqueur `EN_COURS` posé **avant la première écriture** `T_CUMSAL` (écritures non transactionnelles) ; à la fin, statut `SUCCES` et
    instantané (exactement les lignes écrites : matricule, numéro de salarié, variable `CANTINE` ou `FRAIS`, destination = code de constante, unité
    `N` (nombre), valeur ; plage de matricules de l'envoi) dans **une seule transaction SoftTime** ; échec d'une écriture : `ECHEC` ; instantané non
    écrit (période clôturée entre-temps, base indisponible) : `INCERTAIN` et avertissement dans le message. Le message, la confirmation et le
    périmètre de l'envoi ne changent pas. Un salarié absent de SAGE n'est ni écrit ni figé ; une mise à jour sans ligne de cumul est comptée.
  - **Empreinte (format 2)** : le format 1 des heures **ne change pas**. Le format 2, propre à la cantine et aux frais, ne diffère que par la
    première ligne, qui nomme le domaine après la version : `SOFTTIME-PAIE-INSTANTANE|2|{CANTINE ou FRAIS}|{base}|{début}|{fin}|{TYPE}` ; lignes,
    tri, séparateurs, encodage et arrondi sont ceux du format 1. Un couple format / domaine incohérent (1 hors heures, 2 pour les heures) est
    compté comme une altération.
  - **Activation par base** : options `DomaineCantineActif` et `DomaineFraisActif` de `T_PAIE_OPTION`, **désactivées par défaut** ; cases
    « Cantine » et « Frais de déplacement » du bloc « Domaines envoyés à la paie » sous les variables (écran Bases SAGE), enregistrement séparé et
    confirmé, `PUT api/v1/payroll-variables/domains`, même droit que la configuration (administrateur ou `SAGE:{base}:UPDATE`), auteur et date dans
    `T_PAIE_OPTION` et journal central. Pas de verrou de mois : le réglage ne change rien à ce qui est envoyé, seulement ce que la clôture exige.
    Activation refusée pour une base AUTRE. Les droits d'écran `OTHER:CANTEEN` et `OTHER:TRAVEL_EXPENSES` ne changent pas.
  - **Code de constante de la cantine (D6)** : option `CodeConstanteCantine` de `T_PAIE_OPTION` (aucune migration), champ « Code de constante de
    la cantine » du même bloc, même droit, même journal (auteur), caches vidés à l'enregistrement. Sans réglage, le code historique `CANTINE_CODE`
    (repris du desktop) est utilisé : comportement inchangé, mais ce code de 12 caractères ne tient pas dans `T_CST.CodeConstante` (10 caractères
    dans SAGE) et n'existe dans aucune base : l'envoi de la cantine est alors refusé (« Constante SAGE CANTINE_CODE absente… »). Validation à
    l'enregistrement (base STANDARD, refus 400) : code non vide ; longueur au plus celle de `T_CST.CodeConstante`, lue dans le schéma de la base
    active (« Code trop long : 10 caractères au maximum. ») ; constante existante dans `T_CST` avec son opérande (« Ce code n'existe pas dans la
    paie de cette base. »). Un code inchangé n'est pas revérifié. **Activation de la cantine refusée** tant que le code n'est pas valide (« Activation
    de la cantine impossible avec le code … Créez d'abord la constante dans SAGE, 10 caractères au plus, puis saisissez son code. ») : un domaine
    activé est toujours envoyable. Base AUTRE : code non configurable (domaine jamais exigé). L'envoi utilise le code configuré et l'instantané
    enregistre le code réellement utilisé (destination) : formats 1 et 2 de l'empreinte inchangés.
  - **Règle de clôture par domaine** : en plus des heures (règles inchangées, contrôlées en premier), chaque domaine **exigé** doit avoir, sur les
    **bornes exactes** de la période, un dernier envoi `SUCCES` (non `INCERTAIN`), **sans plage de matricules**, avec instantané intègre et concordant
    avec les totaux actuels (`T_CANTINE.TOTAL_CANTINE`, `T_FRAIS.TOTAL_POINT` des mêmes bornes : un « Calculer » relancé après l'envoi est détecté).
    Un domaine est exigé s'il est **activé** pour la base, ou s'il a été **envoyé sur une période qui recoupe** la période même sans être activé
    (sécurité) ; jamais pour une base AUTRE. Refus 409 qui nomme le domaine, par exemple « Cantine : aucun envoi en paie sur les bornes exactes de la
    période (du … au …). Calculez puis envoyez la cantine sur ces bornes, sans plage de matricules, avant de clôturer. » La clôture enregistre
    l'envoi, l'instantané, le format et l'empreinte de chaque domaine exigé dans `T_PAIE_CLOTURE_DOMAINE` (les heures restent dans
    `T_PAIE_CLOTURE`), et le journal `CLOTURE_PAIE` les cite (`Domaines=HEURES, CANTINE:Envoi=…:Format=2:Empreinte=…, FRAIS:…`). Verrous D3
    inchangés.
  - **Écran** : la carte de clôture affiche, par période, l'état de chaque domaine (heures, cantine, frais : envoyé, figé, manquant avec le motif du
    serveur, non exigé), les empreintes à figer dans la confirmation, et toujours aucun bouton de déblocage.
  - **Lecture** : `GET api/v1/payroll-snapshots` rend les envois de tous les domaines, avec leur domaine ; la vérification recalcule l'empreinte
    dans le format du domaine.
  - **Limites** : l'instantané prouve les **lignes écrites**, pas l'état des salariés **sans ligne** : l'envoi de la cantine et des frais ne remet pas
    à 0 le cumul d'un salarié qui n'a pas de total sur la période (il garde la valeur précédente dans SAGE). Les envois de cantine et de frais
    antérieurs à la migration 011 n'ont ni marqueur ni instantané. Changer le code de la cantine après un envoi ne change pas l'instantané figé.
  - **Mise en production de la cantine** (dans cet ordre) : 1) créer dans SAGE la constante de cumul de la cantine (code de 10 caractères au plus,
    avec son opérande) et ses lignes de cumul ; 2) dans SoftTime, écran Bases SAGE, bloc « Domaines envoyés à la paie » : saisir son code et
    enregistrer (le code est vérifié dans SAGE) ; 3) cocher « Cantine » et enregistrer ; 4) calculer puis envoyer la cantine sur les bornes exactes de
    chaque période, sans plage de matricules, avant de la clôturer. Les frais (`HA06`) ne demandent que l'étape 3.
- **Migrations** : `009_instantanes_paie.sql`, `010_cloture_paie.sql` puis `011_domaines_paie.sql` (colonne `DOMAINE` sur `T_PAIE_ENVOI` et
  `T_PAIE_INSTANTANE`, défaut `HEURES`, contrainte CHECK ; table `T_PAIE_CLOTURE_DOMAINE` immuable, erreur 51992), additives et rejouables,
  contrôlées au démarrage comme 007 et 008. **Production sans AutoMigrate** : sauvegarde, puis
  `sqlcmd -S <serveur> -d <base SoftTime> -i migrations/versions/009_instantanes_paie.sql`, puis la même commande avec `010_cloture_paie.sql`, puis
  avec `011_domaines_paie.sql` (dans cet ordre). Sans 011, l'API refuse de démarrer avec un message qui nomme le script.

---

## 6. Divers

- **Cantine** : jours avec pointage 11h–14h30 ; recherche sur `T_CANTINE`, **Calculer** pour régénérer.
  - **Anomalies** : un pointage dans la fenêtre du repas un jour déclaré en absence complète (matin **et**
    après-midi) est signalé comme contradictoire — à revoir avant de pousser la paie.
  - **Synchroniser (SAGE)** : pousse les totaux de la période dans le cumul `T_CUMSAL` référencé par la
    constante `CANTINE_CODE`. **Purger** supprime les lignes de la période (le calcul peut être relancé).
- **Frais de déplacement** : totaux de pointages / corrections sur la période (`T_FRAIS`).
  - **Synchroniser (SAGE)** : pousse les totaux de la période dans le cumul `T_CUMSAL` référencé par la
    constante `HA06`. Disponible uniquement pour une base SAGE de type `STANDARD`.
- **Heures supplémentaires** : bouton **Détail** sur une ligne → jours de la semaine (heures, nuit,
  retard, congé, férié, absence), semaines EXO/IMPO (`T_HSSemaine`) et allocation enregistrée.
- **Congés SAGE** : un jour de congé n'est plus compté comme absence dans le calcul de la semaine
  (`T_HS_VALIDER.ABSENCE`) ; les codes d'événement `0200` / `0300` et les dimanches sont exclus.
- **Pagination** : les listes Traitement et Rapports sont paginées **côté serveur** (50 par page, plafond 100) :
  pointages, heures corrigées, anomalies, shifts, validation semaine, planning, HS, historique, frais, cantine,
  audit et les 6 états de rapports. L'**Export Excel** recharge le jeu complet via l'endpoint non paginé, il n'est
  donc pas limité à la page affichée. La sélection est conservée entre les pages ; « tout sélectionner » porte sur
  la page affichée. Voir `softtime_backend/migrations/add_pagination_performance_indexes.sql` (colonnes bornées +
  index composites) et `benchmark_pagination.sql` (mesure avant/après).

---

## 7. Traçabilité

- **Audit** : colonnes à suivre + journal des événements.

---

## 8. Import / export

- **Import Excel** : planning, shifts, pointages, corrections, affectations.  
- **Import CSV** : correspondances badge ; matricule ; génération de planning par catégorie ou par matricule.  
- **Modèles d'import** : chaque écran d'import propose un bouton **« Modèles d'import »** qui affiche la matrice des
  colonnes attendues (obligatoire, format, exemple, remarque), un exemple de fichier et les règles, avec le
  **modèle téléchargeable** (`.xlsx` ou `.csv`). Les modèles viennent du backend (`GET /api/import-templates`,
  `GET /api/import-templates/{key}`) et sont alignés sur les parseurs : un modèle importé tel quel est accepté
  par l'endpoint correspondant (vérifié par les tests `ImportTemplateTests` / `ImportTemplateImportTests`).
  Les modèles « matrice » (planning) sont datés sur la semaine du lundi courant.
- **Bilan d'import** : après chaque import, le message indique le nombre de lignes importées et de lignes
  ignorées (ex. « 1 affectation(s) importée(s), 2 ligne(s) ignorée(s). »). Un toast d'avertissement (8 s)
  s'affiche dès qu'une ligne est ignorée. Pour les affectations, le message nomme la catégorie ou le
  matricule introuvable (ex. « matricule(s) introuvable(s) : 961 »). Les lignes d'en-tête CSV ne sont pas
  comptées comme ignorées.
- **Export Excel** : grilles listées (états, HS, cantine, frais, etc.) et classeur multi-feuilles du
  tableau de bord analytique.
- **Export PDF** : impression navigateur du tableau de bord analytique (A4, thème clair, toutes les
  lignes filtrées).

---

## Stack

- Frontend : Angular  
- Backend : ASP.NET Core 8  
- Données : SQL Server (SoftTime + SAGE + pointeuse)  
- Prévisions : atterrissage de semaine calculé à la demande (aucune bibliothèque de machine learning)

