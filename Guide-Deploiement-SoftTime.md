# Guide de déploiement et de mise à jour — SoftTime sur IIS

Ce guide explique comment **déployer** SoftTime (backend C# ASP.NET Core 8 + frontend Angular) sur un serveur Windows IIS, puis comment **le mettre à jour** sans rien casser.

> Toutes les commandes serveur se lancent dans **PowerShell en administrateur**.
> Les chemins et noms ci-dessous correspondent au serveur actuel ; adapte-les si besoin.

---

## Sommaire

1. [Architecture](#1-architecture)
2. [Prérequis du serveur](#2-prérequis-du-serveur)
3. [Préparer les livrables (sur ton PC)](#3-préparer-les-livrables-sur-ton-pc)
4. [Configuration de production](#4-configuration-de-production)
5. [Premier déploiement](#5-premier-déploiement)
6. [Mettre à jour l'application](#6-mettre-à-jour-lapplication)
7. [Revenir en arrière (rollback)](#7-revenir-en-arrière-rollback)
8. [Dépannage](#8-dépannage)
9. [Checklists](#9-checklists)

---

## 1. Architecture

La nouvelle version de SoftTime **sépare le frontend et l'API** :

```
Site IIS "SoftTime"   (http :5050  +  https :443 softtime.softwell.cloud)
│   Dossier : C:\SOFT-TIME\SoftTime-Front
│   → IIS sert directement les fichiers Angular (index.html, .js, .css)
│
└── Application "/api-host"
        Dossier : C:\SOFT-TIME\SoftTime-Api
        Pool    : SoftTime-Api (No Managed Code)
        → le backend C# ne gère que l'API
```

| Le navigateur demande… | Qui répond ? |
|---|---|
| `https://softtime.softwell.cloud/` | IIS → `index.html` (front) |
| `/main-xxx.js`, `/assets/...` | IIS → fichier statique (front) |
| `/dashboard`, `/employes/12` (routes Angular) | IIS → `index.html` grâce au `web.config` du front |
| `/api-host/api/auth/login` | Backend C# (sous-application `/api-host`) |

### Pourquoi c'est important

- Le front est compilé avec `apiBaseUrl: "/api-host"` → il appelle l'API sur `/api-host/api/...`.
- Le backend **ne sert plus** le front (les lignes `UseDefaultFiles`, `UseStaticFiles`, `MapFallbackToFile` ont été retirées de `Program.cs`).

👉 Copier la nouvelle version dans l'ancienne structure (un seul site pointant sur le backend, front dans `wwwroot`) **ne marche pas** : le front renvoie 404 et l'API est introuvable sur `/api-host`.

---

## 2. Prérequis du serveur

À vérifier **une seule fois**.

### 2.1 Module ASP.NET Core (Hosting Bundle)

Nécessaire pour qu'IIS sache lancer une application .NET.

```powershell
Get-WebGlobalModule | Where-Object Name -like "*AspNetCore*"
```

Doit afficher `AspNetCoreModuleV2`. Sinon : installer le **.NET 8 Hosting Bundle** (dotnet.microsoft.com → .NET 8 → *Hosting Bundle*), puis `iisreset`.

### 2.2 Module URL Rewrite

Nécessaire pour le `web.config` du front (routes Angular).

```powershell
Get-WebGlobalModule | Where-Object Name -like "*Rewrite*"
```

Doit afficher `RewriteModule`. Sinon : installer **IIS URL Rewrite** (iis.net/downloads/microsoft/url-rewrite).
Sans ce module → **erreur 500.19** sur le front.

### 2.3 Lister les sites existants

```powershell
Import-Module WebAdministration
Get-Website | Select Name, State, PhysicalPath, @{n="Bindings";e={$_.bindings.Collection.bindingInformation}}
```

Situation actuelle :

```
SoftTime   Started   C:\SOFT-TIME\SoftTime-Appli\SoftTime-Appli   {*:5050:, *:443:softtime.softwell.cloud}
```

> ⚠️ Le site a une liaison **HTTPS avec certificat**. Ne jamais modifier ses `bindings` à la main : on change seulement son dossier (voir §5.4).

---

## 3. Préparer les livrables (sur ton PC)

### 3.1 Backend C#

Dans le dossier de la solution (`...\source\softtime_backend`) :

```powershell
git pull origin main

# Vider l'ancienne sortie
Remove-Item .\publish -Recurse -Force -ErrorAction SilentlyContinue

# Publier UNIQUEMENT l'API, en mode autonome
dotnet publish .\SoftTime.Api\SoftTime.Api.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

| Option | Rôle |
|---|---|
| `.\SoftTime.Api\SoftTime.Api.csproj` | Publie **seulement l'API**. Sans ça, le projet de tests est inclus (`SoftTime.Tests.dll`, `testhost.exe`, `coverlet`, `CodeCoverage`… ≈ 500 fichiers inutiles) |
| `-c Release` | Version optimisée |
| `-r win-x64` | Serveur Windows 64 bits |
| `--self-contained true` | Inclut le runtime .NET → pas besoin de .NET installé sur le serveur |
| `-o .\publish` | Dossier de sortie |

**Alternative Visual Studio** : clic droit sur *SoftTime.Api* → *Publier* → *Dossier* → *Afficher tous les paramètres* → Mode de déploiement **Autonome**, Runtime **win-x64**.

> ⚠️ Le dossier `publish` contient ton `appsettings.json` **de développement** (base locale, mot de passe `sa`). Il ne doit pas écraser la config du serveur → voir §4.

### 3.2 Frontend Angular

Dans le dossier du projet Angular (là où se trouve `package.json`) :

```powershell
git pull origin main
npm ci
npx ng build --configuration production
```

Résultat : `dist\<nom-du-projet>\browser\` → c'est **le contenu de ce dossier** qu'on déploie.

Vérifications :

- `src/environments/environment.prod.ts` contient `apiBaseUrl: "/api-host"`.
- Le fichier `web.config` (voir ci-dessous) est présent dans `browser\`. S'il manque, ajoute-le au projet pour qu'il soit copié à chaque build :
  - **Angular 18+** : le mettre dans le dossier `public/` ;
  - **versions antérieures** : le mettre dans `src/` et ajouter `"src/web.config"` aux `"assets"` de `angular.json`.

#### Le `web.config` du front

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <rewrite>
        <rules>
          <rule name="Angular SPA" stopProcessing="true">
            <match url=".*" />
            <conditions logicalGrouping="MatchAll">
              <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
              <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
              <add input="{REQUEST_URI}" pattern="^/api-host" negate="true" />
            </conditions>
            <action type="Rewrite" url="/index.html" />
          </rule>
        </rules>
      </rewrite>
    </system.webServer>
  </location>
</configuration>
```

Ce qu'il fait :

- toute URL qui **n'est pas un fichier ni un dossier réel** (ex. `/dashboard` après un F5) → renvoie `index.html`, et Angular affiche la bonne page ;
- **sauf** `/api-host` → laissé au backend ;
- `inheritInChildApplications="false"` → la règle ne s'applique pas à la sous-application `/api-host`.

---

## 4. Configuration de production

Principe : **la config du serveur vit sur le serveur, jamais dans le package publié.**

| Fichier | Où | Contenu |
|---|---|---|
| `appsettings.json` | dans `publish` (écrasé à chaque mise à jour) | config de développement / valeurs par défaut |
| `appsettings.Production.json` | **uniquement sur le serveur**, créé une fois, jamais écrasé | base du serveur, clé JWT, réglages prod |

Le serveur tourne en environnement `Production` (visible dans les logs : *Hosting environment: Production*), donc `appsettings.Production.json` est lu automatiquement et **remplace** les valeurs d'`appsettings.json`.

Créer `C:\SOFT-TIME\SoftTime-Api\appsettings.Production.json` à partir du modèle `appsettings.Production.json.MODELE` :

```json
{
  "Logging": { "EventLog": { "LogLevel": { "Default": "Warning" } } },
  "ConnectionStrings": {
    "SoftTime": "Data Source=localhost;Initial Catalog=SOFTTIME_DEMO;User ID=COMPTE_SQL;Password=MOT_DE_PASSE;TrustServerCertificate=True;Connect Timeout=60",
    "SoftTimeMl": ""
  },
  "Jwt": {
    "Issuer": "SoftTime",
    "Audience": "SoftTime.Web",
    "Key": "CLE_ALEATOIRE_DE_32_CARACTERES_MINIMUM"
  },
  "Database": { "AutoMigrate": true, "AutoMigrateLockTimeoutSeconds": 60 }
}
```

Points d'attention :

- **Clé JWT obligatoire** (≥ 32 caractères). Si elle est vide → erreur `IDX10703: key length is zero` à chaque requête.
  Générer une clé : `[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }) -as [byte[]])`
  Changer la clé déconnecte tous les utilisateurs (ils doivent se reconnecter).
- **`AutoMigrate: true`** : le backend met à jour le schéma de la base au démarrage. Si la section est absente, les migrations sont désactivées.
- **Sécurité** : éviter le compte `sa` ; créer un compte SQL dédié avec les droits nécessaires sur la base.

---

## 5. Premier déploiement

Principe : **ne jamais toucher au site en production avant d'avoir testé.** On monte un site de test à côté, on valide, puis on bascule.

### 5.1 Sauvegarder la base de données

Dans SQL Server Management Studio : clic droit sur la base → *Tâches* → *Sauvegarder*. Ou :

```sql
BACKUP DATABASE SOFTTIME_DEMO TO DISK = 'C:\backups\SOFTTIME_DEMO_avant_deploiement.bak';
```

> La nouvelle version modifie la base (migrations de schéma, conversion des mots de passe au nouveau format haché). Une fois les mots de passe convertis, **l'ancienne version ne peut probablement plus les lire.**

### 5.2 Vérifier le schéma de la base

La nouvelle version enregistre des mots de passe hachés, plus longs. La colonne `PASSWORD` doit être assez grande, sinon erreur *« Les données de chaîne ou binaires seront tronquées »* (8152) à la connexion.

```sql
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('T_RESPONSABLE', 'suivi_JOURNAL');

-- Si PASSWORD est trop petite (garder NULL / NOT NULL comme avant) :
ALTER TABLE dbo.T_RESPONSABLE ALTER COLUMN PASSWORD nvarchar(256) NULL;
```

### 5.3 Monter un site de test (port 8080)

**a) Vérifier que le port est libre** (aucune ligne affichée = libre) :

```powershell
Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue
```

**b) Copier les fichiers sur le serveur**

| Dossier serveur | Contenu |
|---|---|
| `C:\SOFT-TIME\Test\front` | contenu de `browser\` (avec `web.config`) |
| `C:\SOFT-TIME\Test\api` | contenu de `publish\` + `appsettings.Production.json` (§4) + un dossier vide `logs` |

**c) Activer les logs pendant les tests** : dans `C:\SOFT-TIME\Test\api\web.config`, mettre `stdoutLogEnabled="true"`.

**d) Créer le site de test**

```powershell
Import-Module WebAdministration
$front = "C:\SOFT-TIME\Test\front"
$api   = "C:\SOFT-TIME\Test\api"

New-WebAppPool -Name "SoftTimeTest-Front"
New-WebAppPool -Name "SoftTimeTest-Api"
Set-ItemProperty IIS:\AppPools\SoftTimeTest-Front -Name managedRuntimeVersion -Value ""
Set-ItemProperty IIS:\AppPools\SoftTimeTest-Api   -Name managedRuntimeVersion -Value ""

New-Website -Name "SoftTime-Test" -Port 8080 -PhysicalPath $front -ApplicationPool "SoftTimeTest-Front"
New-WebApplication -Site "SoftTime-Test" -Name "api-host" -PhysicalPath $api -ApplicationPool "SoftTimeTest-Api"

icacls $front /grant "IIS AppPool\SoftTimeTest-Front:(OI)(CI)RX"
icacls $api   /grant "IIS AppPool\SoftTimeTest-Api:(OI)(CI)M"
```

**e) Tester (sur le serveur)**

1. `http://localhost:8080/api-host/api/auth/is-first-run` → réponse JSON = **l'API fonctionne**
2. `http://localhost:8080/` → page de connexion = **le front fonctionne**
3. Se connecter (avec ton propre compte), naviguer, faire **F5** sur une page interne.

En cas d'erreur : lire `C:\SOFT-TIME\Test\api\logs\stdout_*.log` (voir §8).

**Supprimer le site de test** (aucun impact sur la production) :

```powershell
Remove-Website -Name "SoftTime-Test"
Remove-WebAppPool -Name "SoftTimeTest-Front"
Remove-WebAppPool -Name "SoftTimeTest-Api"
```

### 5.4 Basculer la production

On **ne touche pas aux ports ni au certificat** : on change seulement le dossier du site existant et on ajoute la sous-application.

```powershell
Import-Module WebAdministration

# 1. Dossiers définitifs (copie des dossiers de test validés)
Copy-Item "C:\SOFT-TIME\Test\front" "C:\SOFT-TIME\SoftTime-Front" -Recurse
Copy-Item "C:\SOFT-TIME\Test\api"   "C:\SOFT-TIME\SoftTime-Api"   -Recurse

# 2. Pool dédié à l'API
New-WebAppPool -Name "SoftTime-Api"
Set-ItemProperty IIS:\AppPools\SoftTime-Api -Name managedRuntimeVersion -Value ""
icacls "C:\SOFT-TIME\SoftTime-Front" /grant "IIS_IUSRS:(OI)(CI)RX"
icacls "C:\SOFT-TIME\SoftTime-Api"   /grant "IIS AppPool\SoftTime-Api:(OI)(CI)M"

# 3. Bascule
Set-ItemProperty "IIS:\Sites\SoftTime" -Name physicalPath -Value "C:\SOFT-TIME\SoftTime-Front"
New-WebApplication -Site "SoftTime" -Name "api-host" -PhysicalPath "C:\SOFT-TIME\SoftTime-Api" -ApplicationPool "SoftTime-Api"
```

Vérifier `https://softtime.softwell.cloud` (connexion, navigation, F5).

**Annuler la bascule** :

```powershell
Remove-WebApplication -Site "SoftTime" -Name "api-host"
Set-ItemProperty "IIS:\Sites\SoftTime" -Name physicalPath -Value "C:\SOFT-TIME\SoftTime-Appli\SoftTime-Appli"
```

> Pour que ce retour arrière fonctionne, `C:\SOFT-TIME\SoftTime-Appli\SoftTime-Appli` doit contenir **l'ancienne version qui marchait**. Garde ce dossier intact quelques jours avant de le supprimer.

### 5.5 Après validation

- Remettre `stdoutLogEnabled="false"` dans `C:\SOFT-TIME\SoftTime-Api\web.config` (sinon les logs grossissent indéfiniment).
- Supprimer le site de test (§5.3).
- Après quelques jours sans problème : archiver puis supprimer l'ancien dossier `SoftTime-Appli`.

---

## 6. Mettre à jour l'application

Le serveur est déjà configuré : on **sauvegarde, arrête, remplace les fichiers, redémarre**.

### 6.1 Sur ton PC : préparer la nouvelle version

Refaire le §3 :

- backend → dossier `publish\`
- frontend → dossier `browser\`

Copier ces deux dossiers sur le serveur, par exemple dans `C:\SOFT-TIME\Livraisons\<date>\publish` et `...\browser`.

### 6.2 Mettre à jour le backend (API)

```powershell
Import-Module WebAdministration

$site    = "C:\SOFT-TIME\SoftTime-Api"
$pool    = "SoftTime-Api"
$nouveau = "C:\SOFT-TIME\Livraisons\<date>\publish"
$date    = Get-Date -Format "yyyyMMdd-HHmm"

# 1. Sauvegarde
New-Item "C:\backups" -ItemType Directory -Force | Out-Null
Copy-Item $site "C:\backups\SoftTime-Api-$date" -Recurse

# 2. Arrêter le pool (sinon IIS verrouille les DLL)
Stop-WebAppPool -Name $pool
Start-Sleep -Seconds 5

# 3. Vider l'ancien contenu en GARDANT la config prod et les logs
Get-ChildItem $site -Exclude "appsettings.Production.json","logs" | Remove-Item -Recurse -Force

# 4. Copier la nouvelle version (sans écraser la config prod)
Copy-Item "$nouveau\*" $site -Recurse -Force -Exclude "appsettings.Production.json"

# 5. Redémarrer
Start-WebAppPool -Name $pool
```

Vérifier : `https://softtime.softwell.cloud/api-host/api/auth/is-first-run`.

> Si la copie échoue avec « fichier utilisé par un autre processus », augmenter `Start-Sleep` à 10 secondes.
> Variante sans arrêt du pool : déposer un fichier `app_offline.htm` dans le dossier de l'API avant la copie, le supprimer après.

### 6.3 Mettre à jour le frontend

```powershell
$site    = "C:\SOFT-TIME\SoftTime-Front"
$nouveau = "C:\SOFT-TIME\Livraisons\<date>\browser"
$date    = Get-Date -Format "yyyyMMdd-HHmm"

# 1. Sauvegarde
Copy-Item $site "C:\backups\SoftTime-Front-$date" -Recurse

# 2. Vider l'ancien contenu (on garde web.config au cas où le build ne le contient pas)
Get-ChildItem $site -Exclude "web.config" | Remove-Item -Recurse -Force

# 3. Copier le nouveau build
Copy-Item "$nouveau\*" $site -Recurse -Force
```

- Pas besoin d'arrêter le site : ce sont des fichiers statiques.
- On **vide** le dossier avant la copie car chaque build génère des fichiers aux noms différents (`main-H34LRFFZ.js`, `chunk-XXXX.js`…) ; sinon les anciens s'accumulent.
- Après la mise à jour : **Ctrl + F5** dans le navigateur pour vider le cache.

### 6.4 Ordre recommandé

1. Sauvegarder la base de données (§5.1) — surtout si la version contient des changements de base.
2. Mettre à jour le **backend** (§6.2).
3. Mettre à jour le **frontend** (§6.3).
4. Tester : connexion, navigation, F5, une ou deux fonctionnalités modifiées.

---

## 7. Revenir en arrière (rollback)

Remplacer `<date>` par la date de la sauvegarde (`C:\backups\...`).

**Backend** :

```powershell
Stop-WebAppPool -Name "SoftTime-Api"
Start-Sleep -Seconds 5
Get-ChildItem "C:\SOFT-TIME\SoftTime-Api" | Remove-Item -Recurse -Force
Copy-Item "C:\backups\SoftTime-Api-<date>\*" "C:\SOFT-TIME\SoftTime-Api" -Recurse -Force
Start-WebAppPool -Name "SoftTime-Api"
```

**Frontend** :

```powershell
Get-ChildItem "C:\SOFT-TIME\SoftTime-Front" | Remove-Item -Recurse -Force
Copy-Item "C:\backups\SoftTime-Front-<date>\*" "C:\SOFT-TIME\SoftTime-Front" -Recurse -Force
```

> Si la version mise à jour a modifié la base (migrations, conversion de mots de passe), revenir aux anciens fichiers ne suffit pas toujours : il peut falloir restaurer aussi la sauvegarde de la base.

---

## 8. Dépannage

### 8.1 Lire les logs du backend

1. Dans `web.config` de l'API : `stdoutLogEnabled="true"`.
2. Vérifier qu'un dossier `logs` existe à côté et que le pool a le droit d'y écrire.
3. Redémarrer le pool, reproduire l'erreur, lire `logs\stdout_*.log`.

Les nombreux avertissements `No store type was specified for the decimal property...` sont **sans gravité** ; chercher les lignes `fail:` ou `crit:`.

```powershell
Select-String -Path "C:\SOFT-TIME\SoftTime-Api\logs\*.log" -Pattern "fail:|crit:" -Context 0,5
```

### 8.2 Erreurs rencontrées et solutions

| Symptôme | Cause | Solution |
|---|---|---|
| Page blanche / **404** en ouvrant le site | Nouvelle version déployée dans l'ancienne structure : le backend ne sert plus le front | Déployer selon l'architecture §1 (front = racine du site, API = `/api-host`) |
| Le front s'affiche mais les appels API échouent (404 sur `/api-host/...`) | Sous-application `/api-host` absente | `New-WebApplication ... -Name "api-host"` (§5.4) |
| **404 après F5** sur une page interne | `web.config` du front absent | Ajouter le `web.config` du front (§3.2) |
| **500.19** sur le front | Module URL Rewrite non installé | Installer IIS URL Rewrite (§2.2) |
| **500.30 / 500.31 / 500.32** sur l'API | Runtime .NET absent, mauvaise version, ou plantage au démarrage | Publier en `--self-contained` (§3.1) ou installer le Hosting Bundle ; lire les logs |
| `IDX10703: ... key length is zero` | Clé JWT vide dans la config | Renseigner `Jwt:Key` (≥ 32 caractères) dans `appsettings.Production.json` |
| *« Les données de chaîne ou binaires seront tronquées »* (8152) à la connexion | Colonne `PASSWORD` trop courte pour le nouveau hash | `ALTER COLUMN PASSWORD nvarchar(256)` (§5.2) |
| L'API se connecte à la mauvaise base | `appsettings.json` de développement copié sur le serveur, pas d'`appsettings.Production.json` | Créer `appsettings.Production.json` sur le serveur (§4) |
| Copie impossible : « fichier utilisé par un autre processus » | Pool encore actif, DLL verrouillées | `Stop-WebAppPool`, attendre quelques secondes, recommencer |
| Ancienne version toujours visible | Cache du navigateur | Ctrl + F5 |
| Package énorme avec `testhost.exe`, `coverlet`, `SoftTime.Tests.dll` | Publication de la solution entière | Publier uniquement `SoftTime.Api.csproj` (§3.1) |

---

## 9. Checklists

### Premier déploiement

- [ ] Hosting Bundle (`AspNetCoreModuleV2`) et URL Rewrite installés
- [ ] Base de données sauvegardée
- [ ] Colonne `PASSWORD` assez grande
- [ ] Backend publié (API seule, autonome, win-x64)
- [ ] Front compilé (`apiBaseUrl: "/api-host"`, `web.config` présent)
- [ ] `appsettings.Production.json` créé sur le serveur (base, clé JWT, AutoMigrate)
- [ ] Site de test sur 8080 : API OK, front OK, connexion OK, F5 OK
- [ ] Bascule du site `SoftTime` (dossier front + sous-application `api-host`)
- [ ] `https://softtime.softwell.cloud` vérifié
- [ ] Logs stdout désactivés, site de test supprimé
- [ ] Ancien dossier conservé quelques jours

### Chaque mise à jour

- [ ] `git pull` + publication backend + build front
- [ ] Sauvegarde de la base (si changements de base)
- [ ] Sauvegarde des dossiers `SoftTime-Api` et `SoftTime-Front`
- [ ] Backend : stop pool → vider (sauf config prod et logs) → copier → start pool
- [ ] Frontend : vider (sauf web.config) → copier
- [ ] Test : API, connexion, navigation, F5, fonctionnalités modifiées
- [ ] Ctrl + F5 côté utilisateurs si l'ancienne version apparaît encore

### Sécurité

- [ ] Pas de mot de passe de développement dans les fichiers déployés
- [ ] Clé JWT de production aléatoire (pas `SoftTime_Dev_ChangeMe...`)
- [ ] Compte SQL dédié plutôt que `sa`
- [ ] `appsettings.Production.json` jamais commité dans Git
