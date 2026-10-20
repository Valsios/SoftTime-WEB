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
- **Pointages** : la liste est paginée (50 par page, plafond 100). L'**Export Excel** recharge toute la
  période, il n'est donc pas limité à la page affichée.

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
