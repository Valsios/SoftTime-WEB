# Failles de sécurité à corriger : SoftTime

*État au 5 novembre 2026. Sources : audit général de l'application et audit de conformité à la convention C# SOFTWELL (§9).*

## Lire avant tout : quelle branche ?

Les corrections de sécurité sont regroupées sur la branche **`fix/security`**, qui n'est **pas encore fusionnée dans `main`**.

- **Sur `main`** : toutes les failles de ce document sont encore présentes, y compris la section 1.
- **Sur `fix/security`** :
  - les points 1.1 à 1.6 sont corrigés (commit `8435b06` de `fix/refactor-backend`, importé seul) ;
  - les points 3.2 et 3.3 sont corrigés ;
  - les points 1.7 et 1.8 ne le sont pas : ils font partie de la refonte des exceptions (`a7667b2`), restée sur `fix/refactor-backend`.

La première action est donc de faire relire puis fusionner `fix/security` dans `main`.

---

## 1. Corrigé sur `fix/refactor-backend`, encore présent sur `main`

| # | Faille | Risque | Correction faite sur la branche |
|---|---|---|---|
| 1.1 | **Secrets dans le dépôt** : mot de passe `sa` et clé JWT dans `appsettings.json`, avec une valeur de repli pour la clé JWT dans `Program.cs`. | Quiconque connaît la clé peut fabriquer un jeton administrateur. Avec la chaîne de connexion, on accède directement à SQL Server. | Secrets placés dans les user-secrets ou des variables d'environnement. L'API refuse de démarrer sans clé, avec une clé trop courte, ou avec l'ancienne clé publiée. |
| 1.2 | **CORS ouvert** (`AllowAnyOrigin`). | N'importe quel site peut appeler l'API depuis le navigateur d'un utilisateur. | Seules les origines listées dans `Cors:AllowedOrigins` sont acceptées. |
| 1.3 | **JWT de 12 h**, sans refresh token ni révocation, avec `ClockSkew` par défaut. | Un jeton volé reste valable 12 h, et un droit retiré aussi. | Jeton d'accès de 30 min, `ClockSkew = 0`. Refresh token de 7 jours avec rotation, empreinte SHA-256 en base et détection de réutilisation. Endpoints `refresh` et `logout`. |
| 1.4 | **Pas de verrouillage de compte ni de rate limiting** sur `/api/auth/login`. | Force brute sur les mots de passe. | Compte verrouillé 15 min après 5 échecs. 20 requêtes par minute et par IP sur login, refresh et create-admin. |
| 1.5 | **Hachage PBKDF2** (`PasswordHasher` ASP.NET) au lieu de BCrypt, et comparaison des anciens mots de passe en clair sans temps constant. | Non conforme à la convention §9.1, et fuite possible par mesure du temps de réponse. | BCrypt (coût 12), conversion automatique à la connexion, temps de réponse identique pour un login inconnu. |
| 1.6 | **Pas de HSTS, de redirection HTTPS ni d'en-têtes de sécurité**, et Swagger exposé en production. | Interception en HTTP, clickjacking, cartographie de l'API par un attaquant. | HSTS et redirection HTTPS hors développement. En-têtes `X-Content-Type-Options`, `X-Frame-Options`, CSP et `Referrer-Policy`. Swagger en développement seulement. Middlewares dans l'ordre de la convention (§5.7). |
| 1.7 | **Messages d'erreur techniques renvoyés au client** : toute `InvalidOperationException`, y compris celles d'EF Core ou de SqlClient, sortait en 400 avec son texte brut. | Fuite de détails internes (schéma, serveur, parfois une chaîne de connexion). | Commit `a7667b2` : une exception non métier est journalisée côté serveur et renvoyée en 500 avec seulement « Erreur interne. ». |
| 1.8 | **`catch { }` qui avalaient les erreurs**, dont les tests de connexion SAGE qui renvoyaient le message SQL brut. | Erreurs de sécurité invisibles et fuite d'informations. | Commit `a7667b2` : erreurs journalisées, et le client reçoit un message lisible tiré du code d'erreur SQL. |

---

## 2. À faire, hors code : 🔴 urgent

### 2.1 Changer les mots de passe `sa` de SQL Server
- **Constat** : deux mots de passe `sa` sont lisibles dans l'historique git, notamment dans le commit `9b14485`. Les retirer du code ne suffit pas, car l'historique est conservé sur GitHub et dans chaque clone.
- **À faire** :
  1. Changer le mot de passe `sa` sur chaque serveur SQL concerné.
  2. Mieux, créer un compte SQL dédié à l'application, avec seulement les droits nécessaires, au lieu d'utiliser `sa`.
  3. Option : réécrire l'historique (`git filter-repo`) puis forcer la mise à jour de tous les clones. C'est lourd, et cela ne dispense pas du changement de mot de passe.

### 2.2 Considérer comme compromise l'ancienne clé JWT de développement
- **Constat** : l'ancienne clé de développement (`JwtOptions.PublishedDevelopmentKey`) est publiée dans le dépôt, et l'`appsettings.json` local de ce poste la contient encore.
- **À faire** : vérifier qu'aucun environnement déployé ne l'utilise. Sur `fix/refactor-backend`, l'API refuse de démarrer avec cette clé.

### 2.3 Fichiers `appsettings.json` locaux
- **Constat** : depuis le commit `b8e9c2f`, `appsettings.json` est ignoré par git, ce qui est bien. Mais la copie locale de ce poste contient encore le mot de passe `sa` en clair.
- **À faire** : déplacer ces valeurs dans les user-secrets (`dotnet user-secrets set ...`) et garder `appsettings.example.json` comme seul modèle commité.

---

## 3. À corriger dans le code (encore présent sur les deux branches)

### 🟠 3.1 Dépendance vulnérable : AutoMapper 13.0.1
- **Constat** : vulnérabilité de gravité élevée connue, [GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x). Elle est signalée au build.
- **Fichiers** : `SoftTime.Api.csproj` et `SoftTime.Application.csproj`.
- **À faire** : deux options.
  - Mettre AutoMapper à jour, en vérifiant d'abord la licence : les versions récentes sont passées sous licence commerciale.
  - Remplacer AutoMapper par des méthodes de mapping écrites à la main, ou par Mapperly (généré à la compilation, licence libre).

### ✅ 3.2 Connexion : chargement de tous les comptes et login non unique (corrigé sur `fix/security`)

> **Correction :**
> - Une seule requête SQL, sans tenir compte de la casse ni des espaces (`LoginKey`).
> - Avec des doublons hérités, le login exact l'emporte ; sinon la connexion est refusée.
> - La migration `005_unicite_login.sql` pose un index unique sur la clé du login. Si des doublons existent, elle ne le crée pas et les affiche.
> - Un conflit d'insertion simultanée renvoie un 400 sur le champ `login`.
- **Constat** : dans `AuthService.FindByLoginAsync`, si le login exact n'existe pas, l'API charge **toute** la table `T_RESPONSABLE` en mémoire pour comparer sans tenir compte de la casse. Ensuite, `FirstOrDefault()` prend le premier compte trouvé. Aucune contrainte d'unicité sur `LOGIN` n'existe en base.
- **Risques** :
  - Chaque tentative avec un login inconnu coûte un parcours complet de la table. Le rate limiting atténue ce coût, sans le supprimer.
  - Deux comptes avec le même login (casse ou espaces différents) : un seul peut se connecter, et on ne choisit pas lequel.
- **À faire** :
  - Normaliser le login à l'enregistrement (trim, et minuscules ou collation insensible à la casse).
  - Ajouter un index unique sur `LOGIN` dans une migration, après avoir nettoyé les doublons éventuels.
  - Faire une seule requête `WHERE LOGIN = @login`.

### ✅ 3.3 `create-admin` : création concurrente possible (corrigé sur `fix/security`)

> **Correction :**
> - La création s'exécute dans une transaction, sous un verrou `sp_getapplock` (`IUnitOfWork.RunExclusiveAsync`).
> - « Aucun compte n'existe » est revérifié sous le verrou : un second appel simultané est refusé.
- **Constat** : `CreateAdminAsync` vérifie `IsFirstRunAsync()` puis insère, sans transaction ni verrou. Deux requêtes simultanées sur une installation vierge peuvent créer deux administrateurs. L'endpoint est anonyme.
- **Risque** : limité à la fenêtre d'installation, mais un attaquant qui la surveille pourrait créer son propre compte administrateur.
- **À faire** :
  - Exécuter la vérification et l'insertion dans une transaction `Serializable`, ou poser un verrou applicatif SQL (`sp_getapplock`).
  - Option : désactiver cet endpoint par configuration une fois l'installation terminée.

### 🟠 3.4 Jetons stockés dans `localStorage`
- **Constat** : le jeton d'accès et le refresh token sont dans `localStorage` (`softtime_frontend/src/app/core/session.store.ts`).
- **Risque** : une faille XSS, même dans une dépendance tierce, permettrait de voler le refresh token, valable 7 jours. Le code Angular actuel n'utilise ni `innerHTML` ni `bypassSecurity`, et une CSP est en place, ce qui réduit ce risque.
- **À faire** : déplacer le refresh token dans un cookie `HttpOnly; Secure; SameSite=Strict`, limité au chemin `/api/auth`, et garder le jeton d'accès en mémoire seulement.

### 🟡 3.5 Endpoints anonymes en trop
- **Constat** : la convention §9.2 n'autorise `[AllowAnonymous]` que pour login, register et forgot-password. Quatre autres endpoints sont anonymes :

  | Endpoint | Ce qu'il expose | Proposition |
  |---|---|---|
  | `GET /api/meta/endpoints` | La liste de toutes les routes de l'API et des numéros de droits : une carte complète pour un attaquant. | Exiger une authentification, ou ne l'exposer qu'en développement. |
  | `GET /api/auth/is-first-run` | Indique si l'installation est vierge. | Acceptable ; à documenter comme exception. |
  | `POST /api/auth/create-admin` | Création du premier administrateur. | Acceptable si la faille 3.3 est corrigée. |
  | `GET /api/auth/password-policy` | Les règles de mot de passe. | Acceptable : information non sensible. |

### 🟡 3.6 Échecs de connexion non journalisés
- **Constat** : `AuthService` n'a pas d'`ILogger`. Les échecs de connexion, les verrouillages et les réutilisations de refresh token ne sont pas journalisés en Warning, contrairement à ce que demande la convention §6.2.
- **Risque** : on ne voit pas une attaque par force brute ou un vol de jeton.
- **À faire** : injecter `ILogger<AuthService>` et journaliser chacun de ces événements, avec le login et l'IP, mais jamais le mot de passe.

### 🟡 3.7 Validation des entrées incomplète
- **Constat** : seulement 3 validateurs FluentValidation pour environ 100 DTO d'entrée. La validation est éparpillée dans les services, parfois absente : longueurs maximales et formats non contrôlés.
- **À faire** : ajouter des validateurs, en priorité pour les DTO des endpoints d'administration (utilisateurs, rôles, bases SAGE et pointeuse) et pour ceux des imports.

### 🟡 3.8 Identifiants séquentiels
- **Constat** : les entités utilisent des identifiants `int`, `decimal` ou `long` séquentiels, alors que la convention §9.2 demande des GUID.
- **Risque** : on peut deviner les identifiants voisins. Le risque est atténué parce que chaque endpoint vérifie les droits et la base SAGE autorisée. Il ne devient réel que si un endpoint oublie ce contrôle.
- **À faire** : c'est une **décision d'équipe**, car le schéma est hérité de SAGE. À court terme, s'assurer que chaque accès par identifiant vérifie que la ressource appartient bien à une base autorisée pour l'utilisateur.

---

## Vérifié, rien à corriger

- **Injection SQL** : requêtes paramétrées, et liste blanche par expression régulière pour les noms de tables et de colonnes venant de la configuration.
- **Contrôle des droits** : `RequireDroit` sur chaque contrôleur métier, double contrôle sur l'administration, en-tête `X-Sage-Database` obligatoire et vérifié côté serveur.
- **Mots de passe des bases** : ils ne sont jamais renvoyés par l'API (seulement `passwordDefined`).
- **Injection de formules** :
  - L'export CSV du journal neutralise les cellules qui commencent par `=`, `+`, `-`, `@`, une tabulation ou un retour chariot (`csvCell`).
  - Les exports Excel (ClosedXML) écrivent les valeurs comme du texte, jamais comme des formules.
- **Frontend** : aucun `innerHTML` ni `bypassSecurity`, routes protégées par droit.

---

## Ordre de traitement conseillé

1. Fusionner `fix/security` dans `main` (section 1, points 1.1 à 1.6).
2. Changer les mots de passe `sa` et nettoyer les `appsettings.json` locaux (section 2).
3. AutoMapper (3.1). Connexion et unicité des logins (3.2) et `create-admin` (3.3) : faits sur `fix/security`.
4. Refresh token en cookie `HttpOnly` (3.4) et journalisation des échecs de connexion (3.6).
5. `/api/meta` authentifié (3.5), validateurs (3.7), puis la décision sur les GUID (3.8).
