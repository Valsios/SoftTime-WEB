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
- **Protections** : les corrections `ORIGINE = MANUEL` et les semaines validées ne sont jamais écrasées ; seules les corrections `ORIGINE = AUTO` sont purgées puis régénérées.
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
- **Absence** : jour planifié non chômé sans aucune heure corrigée, hors congé et hors férié → `(HD − HA) − pause`.
- **Validation** : seules les lignes cochées passent `VALIDATION_HS = true` et verrouillent `VALIDER_HS` ; une
  semaine validée reste affichée (elle n'est plus recalculée) et peut être annulée, voire purgée.
- **Ventilation** : `T_HSSemaine` est reconstruit à chaque calcul à partir des semaines **validées** —
  8 h à 130 %, au-delà à 150 %.
- **Exonération** : `T_HSExoImp` agrège les semaines validées de la période (max. un mois), plafond de 20 h
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

Période + plage de matricules, export Excel :

- État pointage, absences, retards  
- Récap HS EXO/IMPO  
- Heures par semaine, heures dimanche  
- Congé SAGE du jour  

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
- **Répartition** : onglets **Catégorie / Branche / Département** (barres, anneau, barres) avec
  l'activité de l'axe sélectionné (effectif actif, absences, retards, minutes, HS).
- **Assiduité** : top 5 des salariés les plus et les moins ponctuels de la période (matricule, nom,
  ponctualité, jours pointés, retards, absences).
- Les périodes sans événement sont affichées à 0 : tableaux et courbes restent alignés.
- Les absences « planifiées sans pointage » n'excluent pas encore les congés SAGE (LOT 9) : le taux
  d'absentéisme est donc une borne haute.
- Regroupement et taux sont calculés par `PeriodGrouping` / `DashboardRules` (SoftTime.Domain) et
  couverts par des tests unitaires.

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
- **Import CSV** : correspondances badge ; matricule.  
- **Export Excel** : grilles listées (états, HS, cantine, frais, etc.).

---

## Stack

- Frontend : Angular  
- Backend : ASP.NET Core 8  
- Données : SQL Server (SoftTime + SAGE + pointeuse)
