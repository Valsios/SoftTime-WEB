# Soft Time — Fonctionnalités essentielles

Soft Time est une application web de **gestion des temps et des heures supplémentaires**.  
Elle relie trois univers : la base **SoftTime**, les connexions **SAGE** (paie) et les bases **pointeuse** (badges / CHECKINOUT).

L’accès est filtré par **rôle** (6 droits) et par **société SAGE** active (en-tête `X-Sage-Database`).

---

## 1. Authentification et contexte

- Connexion JWT (utilisateurs `T_RESPONSABLE`).
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
  Une ligne qui ne fait que déclarer une absence (`ABSENCE_AM` / `ABSENCE_PM` sans heure) n'est pas une présence.
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

## 5 ter. Moteur prédictif d'absentéisme et d'heures supplémentaires (ML.NET)

Le moteur ne lit **jamais** les tables fonctionnelles pour s'entraîner : il s'appuie sur une base
analytique dédiée au schéma `ml` (ou une base séparée via `ConnectionStrings:SoftTimeMl`), alimentée
par un ETL qui rejoue les règles existantes (`AbsenceReportRules`, `OvertimeCalculator`).

- **`ml.SalarieJour`** : grain salarié × jour ; features calculées *as-of J-1* (calendrier, salarié,
  historique 7/30/90 jours, collectif). Elle sert à construire les features hebdomadaires HS et à
  écarter les jours sans pointage collectif (trous d'import) de l'entraînement.
- **`ml.SalarieSemaine`** : grain salarié × semaine ISO ; label HS = valeur validée de
  `T_HS_VALIDER` si elle existe, sinon résultat de `OvertimeCalculator`.
- **`ml.Modele`** : registre versionné (binaire ML.NET, hash du contrat de features, métriques,
  statut). Un seul modèle actif par société et par type.
- **`ml.Prediction`** : prédictions précalculées (HS par semaine) et relues par l'API.

Deux modèles par société : **P(HS > 0)** (classification) et **montant HS**
(régression). Entraînement `FastTree` (ML.NET, sans dépendance native) sur les 18 derniers mois,
découpage **chronologique** (dernières semaines en test) et **porte qualité** : un modèle n'est
activé que s'il bat la baseline statistique ; sinon l'ancien modèle reste actif et l'écran affiche
« Baseline ».

- **Rafraîchissement** : service d'arrière-plan (quotidien) + jobs admin. Fenêtre glissante de
  12 semaines pour absorber les corrections tardives ; backfill complet par trimestres au premier
  déploiement. Les lignes anciennes restent figées (snapshot).
- **Page `/predictions`** (États, permission `REPORTS:PREDICTIONS`) : état du moteur, **horizon de 4
  semaines** (total prévu et salariés au-delà de 20 h par semaine, « planning non saisi » quand rien n'est prévisible ; un clic
  choisit la semaine analysée), HS prévues de la
  semaine choisie (total et fourchette, salariés au-delà du plafond exonéré de 20 h —
  `OvertimeRules.ExemptCeilingHours`, au-delà les HS deviennent imposables —, département le plus
  chargé, précision —
  constatée dès qu'une semaine est comparable, sinon mesurée à l'entraînement
  — en heures), vue **par département** (barre, part du total ; un clic filtre la liste) puis détail
  par salarié avec fourchette et probabilité d'HS, **prévu / réel** des 8 dernières semaines
  terminées (écart moyen par salarié, part du réel dans la fourchette, biais du total ; la dernière
  prévision émise pour la semaine est comparée aux HS de `ml.SalarieSemaine`), historique des
  modèles. Les prévisions passées sont conservées sur `HistoryMonths` : la purge des anciens
  modèles ne retire que leur binaire tant que des prévisions y renvoient. La prévision d'absence
  a été retirée (peu exploitable) ; les modèles d'absence déjà entraînés restent en base, ignorés.
- **API** : `GET /api/predictions/status|job|overtime|overtime/horizon|overtime/accuracy`, `POST /api/predictions/refresh|train|backfill`.
- **Migrations** : EF Core versionnées (`SoftTime.Infrastructure/Migrations/Ml`,
  `AddMlPredictionSchema`) et script idempotent `migrations/AddMlPredictionSchema.sql`
  (voir `migrations/README.md`).
- **Limites assumées** : historique insuffisant ⇒ baseline ; ancienneté approximée par
  `T_CARDPAIE.DATE` ; prédictions individuelles réservées à l'aide à la planification (pas de
  sanction automatique).

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

  Les quatre niveaux sont Information (poids 0), Mineure (1), Importante (3) et Critique (5). Une
  anomalie « Information » figure dans les comptages mais n'entre ni dans le score ni dans le statut.
- **Score** : `100 − 100 × (somme des poids) ÷ (poids maximum × jours-employé contrôlés)`, borné entre 0
  et 100 ; aucun score sans jour contrôlé.
- **Statut** : *Prêt* (aucune anomalie critique), *Bloquant* (critiques ÷ jours-employé contrôlés ≥ **5 %**),
  sinon *À corriger*. **Le seuil de 5 % est provisoire**, à confirmer par le manager (`Closing:CriticalRatioBlocking`).
  « Bloquant » est un statut d'affichage : aucun autre écran n'est empêché.
- **Filtre et tri par gravité** : les quatre cartes de comptage servent de bascules de filtre de la liste,
  et un menu trie la liste du plus grave au moins grave (puis date, matricule, type). Seule la liste change :
  les comptages, le score et le statut restent ceux de l'analyse complète. Changer le filtre ou le tri relit
  la page 1 dans l'analyse en cache du serveur, sans nouvelle analyse (`gravites=…`, `tri=gravite`).
- **Export Excel** : le bouton « Exporter » produit un classeur (feuille « Synthèse » avec période, département,
  statut, score, comptages, filtre et tri appliqués ; feuille « Anomalies » avec toute la liste affichée, toutes
  les pages). Il suit l'onglet, le filtre de gravité et le tri affichés, et refuse d'exporter si les données ont
  changé depuis l'analyse.
- **Filtre figé en session** : le filtre saisi, le dernier résultat (avec la date et l'heure de l'analyse), l'onglet,
  le filtre de gravité et le tri sont conservés en mémoire tant que la session dure. Aucune analyse ne se lance à
  l'ouverture de la page ni au retour : il faut cliquer sur « Analyser ». Un filtre modifié affiche « Filtre
  modifié, réanalyser ». L'état est vidé à la déconnexion et au changement de compte ou de base active.
- **Avancement de la paie** (`GET api/closing/checklist`, séparé de l'analyse, même droit) : chargé avec
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
  coupée : toutes les routes `api/me/space/*` répondent 404.
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
  compte supprimé ou sans `SELF:READ`. Seules les routes `api/me/space/*` font cette lecture ; aucune autre route n'est touchée.
- **Matricule partagé = refus.** Si un autre compte porte la même clé de matricule (casse, espaces et zéros de tête ignorés), **aucun** des
  porteurs ne lit Mon espace (403 « matricule porté par plusieurs comptes : contactez la RH »), sans citer l'autre compte. À la création des
  comptes Salarié, donner un matricule unique.
- **Aucune conservation par le navigateur.** Toutes les réponses de `api/me/space/*` (succès, 400, 403, 404 de la fonction désactivée)
  portent `Cache-Control: no-store` et `Pragma: no-cache`. Seuls les 401 (jeton absent, rendus avant le contrôleur) et les 405 n'en portent pas :
  ils ne contiennent aucune donnée.
- **Congés SAGE : une seule lecture partagée.** La lecture du tenant (qui prend plusieurs secondes) n'est lancée qu'une fois à la fois par
  base, quel que soit le nombre de salariés qui la demandent ; les autres attendent la même lecture. Elle est conservée **5 minutes** en
  mémoire, dans le cache seulement : aucune route ne la rend, chaque salarié n'en reçoit qu'une copie de sa propre part (clé utilisateur |
  base | matricule, jamais plus vieille que la lecture). Un échec n'est jamais conservé. Plafond mémoire : 8 bases et 512 parts de salarié ;
  mesuré environ 1,3 Mo pour la démo (3 008 cartes).
- **Lectures filtrées dans la requête.** Plannings, anomalies et catégories du salarié sont filtrés dans la requête SQL (et recontrôlés par la
  clé canonique) au lieu d'être lus pour tout le tenant puis filtrés.
- **Erreurs affichées durablement (écran).** Une erreur de `api/me/space/*` s'affiche dans un encadré au-dessus des cartes, en texte seul (jamais le
  JSON brut), avec le message du serveur et une consigne : « Contactez la RH. » (403 de compte), « Reconnectez-vous. » (message demandant de se
  reconnecter), « Choisissez un autre mois. » (400), « Réessayez dans un instant. » (erreur serveur ou réseau). Les erreurs de compte, de période ou de
  session remplacent les quatre cartes ; avec l'option coupée (404 « Mon espace n'est pas activé »), l'encadré indique « Mon espace n'est pas encore activé :
  contactez la RH. ». Le menu et la redirection après connexion ne changent pas (ils dépendent de `SELF:READ`, pas de l'option serveur).
- **Semaine à cheval.** Une semaine qui chevauche deux mois apparaît dans chacun (le filtre retient les semaines dont le lundi est dans la période élargie au lundi
  précédent) : une note sous la carte « Mes heures par semaine » le rappelle, les totaux de deux mois ne doivent donc pas être additionnés.

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
- Moteur prédictif : ML.NET 5 (`Microsoft.ML`, `Microsoft.ML.FastTree`)

