# Failles de sécurité : SoftTime

*État au 5 novembre 2026. Sources : audit général de l'application et audit de conformité à la convention C# SOFTWELL (§9).*

## Lire avant tout : quelle branche ?

Les corrections sont regroupées sur la branche **`fix/security`**, qui n'est **pas encore fusionnée dans `main`**.

- **Sur `main`** : toutes les failles des sections 1 et 3 sont encore présentes.
- **Sur `fix/security`** : il reste seulement deux points, qui demandent une décision (3.1 et 3.8), et les actions hors code de la section 2.

La première action est donc de faire relire puis fusionner `fix/security` dans `main`.

---

## 1. Corrigé sur `fix/security`

| # | Faille | Risque | Correction |
|---|---|---|---|
| 1.1 | **Secrets dans le dépôt** : mot de passe `sa` et clé JWT dans `appsettings.json`, avec une valeur de repli pour la clé JWT dans `Program.cs`. | Quiconque connaît la clé peut fabriquer un jeton administrateur. Avec la chaîne de connexion, on accède directement à SQL Server. | Secrets dans les user-secrets ou des variables d'environnement. L'API refuse de démarrer sans clé, avec une clé trop courte ou avec l'ancienne clé publiée. |
| 1.2 | **CORS ouvert** (`AllowAnyOrigin`). | N'importe quel site peut appeler l'API depuis le navigateur d'un utilisateur. | Seules les origines listées dans `Cors:AllowedOrigins` sont acceptées. |
| 1.3 | **JWT de 12 h**, sans refresh token ni révocation, avec `ClockSkew` par défaut. | Un jeton volé reste valable 12 h, et un droit retiré aussi. | Jeton d'accès de 30 min, `ClockSkew = 0`. Refresh token de 7 jours avec rotation, empreinte SHA-256 en base et détection de réutilisation. |
| 1.4 | **Pas de verrouillage de compte ni de rate limiting** sur `/api/auth/login`. | Force brute sur les mots de passe. | Compte verrouillé 15 min après 5 échecs. 20 requêtes par minute et par IP sur login, refresh, logout et create-admin. |
| 1.5 | **Hachage PBKDF2** au lieu de BCrypt, et comparaison des anciens mots de passe en clair sans temps constant. | Non conforme à la convention §9.1, et fuite possible par mesure du temps de réponse. | BCrypt (coût 12), conversion automatique à la connexion, même temps de réponse pour un login inconnu. |
| 1.6 | **Pas de HSTS, de redirection HTTPS ni d'en-têtes de sécurité**, et Swagger exposé en production. | Interception en HTTP, clickjacking, cartographie de l'API. | HSTS et redirection HTTPS hors développement. En-têtes de sécurité (`nosniff`, `X-Frame-Options`, CSP, `Referrer-Policy`). Swagger en développement seulement. |
| 1.7 | **Messages d'erreur techniques renvoyés au client** : toute `InvalidOperationException`, `KeyNotFoundException` ou `UnauthorizedAccessException`, y compris celles d'EF Core, de SqlClient ou de .NET, sortait avec son texte brut. | Fuite du schéma, d'un serveur, d'un chemin ou d'un nom de compte. | Le code SoftTime lève des exceptions typées dérivées de `DomainException` (`NotFoundException`, `ForbiddenException`…) ; le middleware ne renvoie que leur message. Toute autre erreur est journalisée (méthode, chemin, pile) et renvoyée en 500 avec seulement « Erreur interne. ». |
| 1.8 | **Tests de connexion qui renvoyaient le message SQL brut**, et lecture du registre SQL avec un `catch { }` muet. | Fuite d'informations, erreurs invisibles. | Les erreurs SQL de découverte deviennent une cause lisible tirée de leur numéro (identifiants refusés, serveur injoignable…), et le détail est journalisé. Le `catch` du registre est limité aux erreurs d'accès et journalisé. Les autres `catch` muets restent sur `fix/refactor-backend` (`a7667b2`) : ils posent un problème de qualité, pas de sécurité. |
| 3.2 | **Connexion** : un login inconnu chargeait toute la table des comptes, et deux comptes pouvaient partager un login. | Coût d'une requête par tentative, et un compte ouvert au hasard en cas de doublon. | Une seule requête SQL, sans casse ni espaces (`LoginKey`). Avec des doublons hérités, le login exact l'emporte ; sinon la connexion est refusée. Migration `005_unicite_login.sql` : index unique, non créé (avec la liste affichée) s'il existe des doublons. |
| 3.3 | **`create-admin` concurrent** : deux requêtes simultanées pouvaient créer deux administrateurs. | Un attaquant qui surveille l'installation peut créer son propre administrateur. | Création dans une transaction, sous verrou `sp_getapplock`, avec nouvelle vérification sous le verrou. |
| 3.4 | **Jetons dans `localStorage`**. | Une faille XSS pouvait voler le refresh token, valable 7 jours. | Refresh token dans un cookie `HttpOnly`, `SameSite=Strict` (`Secure` en HTTPS), jamais dans le corps des réponses. Jeton d'accès en mémoire seulement ; après un rechargement de la page, le premier appel est rafraîchi grâce au cookie. Les anciennes clés `st_token` et `st_refresh` sont purgées. |
| 3.5 | **`/api/meta/endpoints` anonyme**. | Carte complète des routes et des numéros de droits pour un attaquant. | Authentification exigée. |
| 3.6 | **Événements de connexion non journalisés**. | Force brute ou vol de jeton invisibles. | Avertissements journalisés : login inconnu ou ambigu, mot de passe incorrect, compte verrouillé, verrouillage déclenché, réutilisation d'un refresh token. Jamais le mot de passe ; le login saisi est nettoyé et tronqué. |
| 3.7 | **Validation des entrées absente** : les 3 validateurs existants n'étaient jamais exécutés. | Valeurs démesurées ou mal formées traitées sans contrôle. | Filtre MVC qui exécute tout validateur enregistré (400 `{ error, field }`). Nouveaux validateurs : connexion, refresh, admin initial, utilisateurs, rôles, bases SAGE et pointeuse, tests de connexion (longueurs, caractères de contrôle, e-mail, type de base). |

Détail de 3.4 : le cookie est posé sur `Path=/` et non sur `/api/auth`, parce qu'en production l'API est servie derrière un préfixe (`/api-host`) que le serveur ne connaît pas. `SameSite=Strict` empêche tout autre site de s'en servir.

---

## 2. À faire, hors code : 🔴 urgent

### 2.1 Changer les mots de passe `sa` de SQL Server
- **Constat** : deux mots de passe `sa` sont lisibles dans l'historique git, notamment dans le commit `9b14485`. Les retirer du code ne suffit pas, car l'historique est conservé sur GitHub et dans chaque clone.
- **À faire** :
  1. Changer le mot de passe `sa` sur chaque serveur SQL concerné.
  2. Mieux, créer un compte SQL dédié à l'application, avec seulement les droits nécessaires, au lieu d'utiliser `sa`.
  3. Option : réécrire l'historique (`git filter-repo`) puis forcer la mise à jour de tous les clones. C'est lourd, et cela ne dispense pas du changement de mot de passe.

### 2.2 Considérer comme compromise l'ancienne clé JWT de développement
- **Constat** : l'ancienne clé de développement (`JwtOptions.PublishedDevelopmentKey`) est publiée dans le dépôt.
- **À faire** : vérifier qu'aucun environnement déployé ne l'utilise. Sur `fix/security`, l'API refuse de démarrer avec cette clé exacte.

### 2.3 Fichiers `appsettings.json` locaux
- **Constat** : depuis le commit `b8e9c2f`, `appsettings.json` est ignoré par git, ce qui est bien. Mais la copie locale de ce poste contient encore le mot de passe `sa` en clair, et une clé JWT proche de l'ancienne clé publiée.
- **À faire** : déplacer ces valeurs dans les user-secrets (`dotnet user-secrets set ...`), avec une clé JWT aléatoire, et garder `appsettings.example.json` comme seul modèle commité.

---

## 3. Reste à décider

### 🟠 3.1 Dépendance vulnérable : AutoMapper 13.0.1
- **Constat** : [GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x) (CVE-2026-32933, gravité élevée). AutoMapper descend dans les objets imbriqués sans limite de profondeur : un objet très profond fait déborder la pile et arrête tout le processus.
- **Versions corrigées** : 15.1.1 et 16.1.1. Elles sont sous licence commerciale (gratuite seulement sous conditions).
- **Exposition réelle dans SoftTime** : faible. Les objets mappés sont des entités et des DTO plats, sans type récursif. Les corps de requête sont lus par System.Text.Json, limité à 64 niveaux.
- **Options** :
  1. Passer à AutoMapper 15.1.1 ou plus, avec une licence.
  2. Remplacer AutoMapper par des méthodes de mapping écrites à la main, ou par Mapperly (généré à la compilation, licence libre). Il y a un profil de 97 lignes, environ 40 appels et 18 fichiers de test qui construisent le mapper.
  3. Accepter le risque en le documentant et masquer cet avertissement précis (`NuGetAuditSuppress`).

### 🟡 3.8 Identifiants séquentiels
- **Constat** : les entités utilisent des identifiants `int`, `decimal` ou `long` séquentiels, alors que la convention §9.2 demande des GUID.
- **Risque** : on peut deviner les identifiants voisins. Le risque est atténué parce que chaque endpoint vérifie les droits et la base SAGE autorisée ; il ne devient réel que si un endpoint oublie ce contrôle.
- **À faire** : c'est une **décision d'équipe**, car le schéma est hérité de SAGE. À court terme, s'assurer que chaque accès par identifiant vérifie que la ressource appartient bien à une base autorisée pour l'utilisateur.

### Endpoints anonymes restants (convention §9.2)
`is-first-run`, `create-admin` et `password-policy` restent anonymes : ils sont nécessaires avant toute connexion. `create-admin` refuse dès qu'un compte existe et s'exécute sous verrou. Ce sont des exceptions à documenter dans la convention.

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

1. Fusionner `fix/security` dans `main`, après relecture et un essai dans l'application réelle.
2. Changer les mots de passe `sa` et nettoyer les `appsettings.json` locaux (section 2).
3. Décider pour AutoMapper (3.1).
4. Décider pour les GUID (3.8) et documenter les endpoints anonymes restants.
