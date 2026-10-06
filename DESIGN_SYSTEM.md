# SoftTime — Design System Reference

## 1. Police

- **Famille** : `'Plus Jakarta Sans'`, `'DM Sans'`, `'Inter'`, `system-ui`
- **Taille de base** : 14 px, `line-height: 1.5`
- **Lissage** : `-webkit-font-smoothing: antialiased`
- Titres (`h1–h4`) : `font-weight: 700`

---

## 2. Palette de couleurs (Light mode)

### Brand — Bleu

| Token              | Valeur                    |
|--------------------|---------------------------|
| `--softtime`       | `#2563eb`                 |
| `--softtime-light` | `#eff6ff`                 |
| `--softtime-dark`  | `#1e3a8a`                 |
| `--softtime-glow`  | `rgba(37, 99, 235, 0.45)` |
| `--primary-50`     | `#f0f6ff`                 |
| `--primary-100`    | `#e0ecff`                 |
| `--primary-200`    | `#bfdbfe`                 |
| `--primary-300`    | `#93c5fd`                 |
| `--primary-400`    | `#60a5fa`                 |
| `--primary-500`    | `#2563eb`                 |
| `--primary-600`    | `#1d4ed8`                 |
| `--primary-700`    | `#1e40af`                 |
| `--primary-800/900`| `#1e3a8a`                 |
| `--primary-950`    | `#172554`                 |

### Accent — Orange

| Token          | Valeur      |
|----------------|-------------|
| `--accent-500` | `#f8b54a`   |
| `--accent-600` | `#f5a623`   |
| `--accent-700` | `#d48820`   |
| `--accent-800` | `#b3691d`   |
| `--accent-900` | `#924c1a`   |

### Surfaces & Neutres

| Token                | Valeur                       |
|----------------------|------------------------------|
| `--app-bg`           | `#f4f6f9`                    |
| `--app-surface`      | `#ffffff`                    |
| `--app-surface-soft` | `#f8faff`                    |
| `--surface-light`    | `#f9fafb`                    |
| `--surface-dark`     | `#f3f4f6`                    |
| `--app-border`       | `rgba(15, 23, 42, 0.07)`     |
| `--color-border`     | `#e5e7eb`                    |
| `--border-light`     | `#f3f4f6`                    |
| `--app-text`         | `#212529`                    |
| `--text-secondary`   | `#6b7280`                    |
| `--text-muted`       | `#9ca3af`                    |

### États

| Token       | Couleur    | Fond        |
|-------------|------------|-------------|
| `--success` | `#10b981`  | `#f0fdf4`   |
| `--warning` | `#f59e0b`  | `#fffbeb`   |
| `--danger`  | `#ef4444`  | `#fef2f2`   |
| `--info`    | `#06b6d4`  | `#f0f9ff`   |

---

## 3. Dark mode (`[data-theme='dark']`)

| Token                | Valeur                        |
|----------------------|-------------------------------|
| `--softtime`         | `#60a5fa`                     |
| `--softtime-light`   | `rgba(37, 99, 235, 0.14)`     |
| `--softtime-dark`    | `#93c5fd`                     |
| `--app-bg`           | `#070b18`                     |
| `--app-surface`      | `#12182b`                     |
| `--app-surface-soft` | `#18213a`                     |
| `--app-border`       | `rgba(148, 163, 184, 0.2)`    |
| `--color-border`     | `rgba(148, 163, 184, 0.2)`    |
| `--app-text`         | `#f4f7ff`                     |
| `--text-secondary`   | `#a9b4d5`                     |
| `--text-muted`       | `#8390b5`                     |

---

## 4. Mise en page générale

La page est divisée en deux colonnes via CSS Grid :

```
+------------------+-------------------------------+
|   Sidebar        |  Topbar (header)              |
|   236px          +-------------------------------+
|   (mini: 54px)   |  <router-outlet> (contenu)    |
|                  |  padding: 24px                |
+------------------+-------------------------------+
```

- `height: 100vh`, `overflow: hidden`
- Transition collapse : `grid-template-columns 250ms cubic-bezier(0, 0, 0.2, 1)`

---

## 5. Sidebar (navigation gauche)

| Propriété          | Valeur                             |
|--------------------|------------------------------------|
| Fond               | `rgb(26, 38, 52)` (bleu nuit)      |
| Largeur normale    | `236px`                            |
| Largeur mini       | `54px` (icônes seules)             |
| Texte              | `rgba(255, 255, 255, 0.8)`         |
| Texte atténué      | `rgba(255, 255, 255, 0.38)`        |
| Item hover fond    | `rgba(255, 255, 255, 0.04)`        |
| Item actif fond    | `rgba(74, 144, 217, 0.15)`         |
| Item actif couleur | `#4a90d9`                          |
| Item padding       | `9px 10px`                         |
| Item border-radius | `8px`                              |
| Item font-size     | `13.5px`, `font-weight: 500`       |
| Titre de section   | `10px`, uppercase, `letter-spacing: 0.08em` |
| Gap entre items    | `2px`                              |
| Nav padding        | `8px`                              |

**Comportement accordéon :**
- Une seule section ouverte à la fois.
- Clic sur le titre toggle l'ouverture ; un chevron tourne à 180° quand ouvert.
- La section contenant la route active s'ouvre automatiquement à la navigation.
- En mode mini (icônes seules), les titres disparaissent et tous les items restent visibles.

---

## 6. Topbar (header)

| Propriété     | Valeur                                        |
|---------------|-----------------------------------------------|
| Hauteur       | `78px`                                        |
| Fond          | `rgba(255, 255, 255, 0.94)` + `blur(12px)`    |
| Bordure basse | `1px solid var(--app-border)`                 |
| Ombre         | `0 10px 30px rgba(31, 41, 90, 0.06)`          |
| Z-index       | `1010`                                        |
| Dark mode     | `rgba(18, 24, 43, 0.9)`                       |
| Boutons icône | `38×38px`, `border-radius: 10px`, bordure fine |

---

## 7. Espacement

| Token        | Valeur |
|-------------|--------|
| `--space-xs` | 4px    |
| `--space-sm` | 8px    |
| `--space-md` | 12px   |
| `--space-lg` | 16px   |
| `--space-xl` | 24px   |
| `--space-2xl`| 32px   |

---

## 8. Border-radius

| Token           | Valeur |
|----------------|--------|
| `--radius-xs`   | 2px    |
| `--radius-sm`   | 4px    |
| `--radius-md`   | 8px    |
| `--radius-lg`   | 12px   |
| `--radius-xl`   | 16px   |
| `--radius-2xl`  | 22px   |
| `--radius-full` | 9999px |

---

## 9. Ombres

| Token             | Valeur                                   |
|-------------------|------------------------------------------|
| `--shadow-sm`     | `0 1px 2px rgba(0, 0, 0, 0.05)`         |
| `--shadow-md`     | `0 4px 6px rgba(0, 0, 0, 0.1)`          |
| `--shadow-lg`     | `0 10px 15px rgba(0, 0, 0, 0.1)`        |
| `--shadow-xl`     | `0 20px 25px rgba(0, 0, 0, 0.2)`        |
| `--shadow-card`   | `0 4px 8px rgba(0, 0, 0, 0.08)`         |
| `--shadow-elevate`| `0 12px 24px rgba(0, 0, 0, 0.12)`       |
| `--shadow-inset`  | `inset 0 2px 4px rgba(0, 0, 0, 0.05)`  |

---

## 10. Motion / Transitions

| Token        | Valeur                          |
|-------------|---------------------------------|
| `--t-fast`   | `150ms`                         |
| `--t-normal` | `250ms`                         |
| `--t-slow`   | `350ms`                         |
| `--ease`     | `cubic-bezier(0, 0, 0.2, 1)`   |

**Animations nommées :**

| Nom               | Effet                                        |
|-------------------|----------------------------------------------|
| `soft-fade`       | opacity 0 → 1                                |
| `soft-fade-in`    | opacity + `translateY(8px)` → 0             |
| `soft-slide-in`   | opacity + `translateX(-10px)` → 0           |
| `soft-slide-up`   | opacity + `translateY(24px)` → 0            |
| `soft-spin`       | rotation 360° (spinners)                    |

Classe utilitaire : `.soft-fade-in` applique `soft-fade-in` sur `--t-slow`.

---

## 11. Composants UI (`soft-*`)

### Boutons — `.soft-btn`

- `padding: 9px 16px` (normal) · `6px 12px` (sm) · `12px 22px` (lg)
- `border-radius: 8px` · `font-weight: 600` · `font-size: 14px`
- Hover : `translateY(-1px)`
- Focus ring : `0 0 0 4px rgba(37, 99, 235, 0.2)`
- Disabled : `opacity: 0.5`

| Variante        | Fond / couleur                         |
|-----------------|----------------------------------------|
| `--primary`     | `#2563eb` + glow bleu / blanc          |
| `--secondary`   | `surface-dark` + bordure / texte       |
| `--accent`      | `#f5a623` / blanc                      |
| `--danger`      | `#ef4444` / blanc                      |
| `--ghost`       | transparent / `text-secondary`         |

### Cartes — `.soft-card`

- Fond : `--app-surface` · bordure : `--app-border` · `border-radius: 16px`
- Ombre : `--shadow-sm`
- `--elevated` : sans bordure + `--shadow-lg`
- `--interactive` : hover → `--shadow-md` + `border-color: primary-200`
- Padding : `--pad-sm` 12px · `--pad-md` 16px · `--pad-lg` 24px

### Champs — `.soft-input` / `.soft-select`

- Bordure : `1px solid --color-border` · `border-radius: 4px` · `padding: 8px 12px`
- Focus : `border-color: --softtime` + `box-shadow: 0 0 0 3px rgba(37,99,235,0.1)`
- Erreur (`.soft-field--error`) : bordure et ombre rouges
- Label : `font-weight: 600`, `font-size: 14px`
- Hint / erreur : `font-size: 12px`

### Toggle — `.soft-toggle`

- `44×24px` · thumb `16×16px` blanc
- OFF : fond `#d1d5db` · ON : fond `--softtime`
- Thumb translate : `20px`

### Badges — `.soft-badge`

- `border-radius: 9999px` · `font-size: 12px` · `font-weight: 700` · `padding: 2.5px 10px`

| Variante     | Fond               | Texte            |
|--------------|--------------------|------------------|
| `--primary`  | `--primary-50`     | `--primary-700`  |
| `--success`  | `--success-bg`     | `--success`      |
| `--warning`  | `--warning-bg`     | `--warning`      |
| `--danger`   | `--danger-bg`      | `--danger`       |
| `--info`     | `--info-bg`        | `--info`         |
| `--orange`   | `#fff7ed`          | `#c2410c`        |
| `--muted`    | `--surface-dark`   | `--text-secondary` |

### Tableaux — `.soft-table`

- Wrap : `border-radius: 12px`, `overflow-x: auto`
- Header : fond `surface-dark` · texte `text-secondary` · 12px uppercase · `font-weight: 700` · `padding: 12px 16px`
- Cellules : `padding: 12px 16px` · séparateur `--border-light`
- Hover ligne : fond `--app-surface-soft`
- Vide : centré, `padding: 40px`, `color: --text-muted`

### Modales — `.soft-modal`

- Overlay : `rgba(0,0,0,0.45)` + `backdrop-filter: blur(12px)` · animation `soft-fade`
- Panel : `border-radius: 16px` · `--shadow-xl` · animation `soft-slide-up`
- Tailles : `--sm` 400px · défaut 560px · `--lg` 720px · `--xl` 960px
- Body : `max-height: 65vh`, overflow-y scroll
- Header / footer : `padding: 16px 24px` · `border: 1px solid --app-border`

### Toasts — `.soft-toast`

- Position fixe : `top: 20px` / `right: 20px`
- Largeur : 260–380px · `padding: 12px 16px`
- `border-radius: 8px` · `border-left: 4px solid` (couleur type) · `--shadow-lg`
- Animation : `soft-slide-in`

| Variante     | Couleur bord gauche |
|--------------|---------------------|
| défaut       | `--softtime`        |
| `--success`  | `--success`         |
| `--error`    | `--danger`          |
| `--warning`  | `--warning`         |
| `--info`     | `--info`            |

---

## 12. Icônes

Style **Feather** (stroke SVG uniquement) :
- `stroke-width: 2` · `stroke-linecap: round` · `stroke-linejoin: round`
- `fill: none`
- ViewBox : `0 0 24 24`
- Taille par défaut : `18px`
- Couleur héritée via `currentColor`

---

## 13. Z-index

| Couche            | Valeur |
|-------------------|--------|
| `--z-dropdown`    | 1000   |
| `--z-sticky`      | 1010   |
| `--z-fixed`       | 1020   |
| `--z-popover`     | 1040   |
| `--z-tooltip`     | 1050   |
| `--z-notification`| 1060   |
| `--z-modal`       | 2000   |

---

## 14. Scrollbar personnalisée

- Largeur : `10px`
- Thumb : `--app-border` · `border-radius: 9999px`
- Thumb hover : `--text-muted`

---

## 15. Classes utilitaires

| Classe           | Effet                            |
|------------------|----------------------------------|
| `.soft-fade-in`  | Animation entrée (opacity + Y)   |
| `.soft-page-header` | Flex, space-between, mb 24px  |
| `.soft-toolbar`  | Flex wrap, gap 12px, mb 16px    |
| `.soft-spinner-lg` | Spinner 34px, bleu             |
| `.soft-empty`    | Centré, padding 48px, muted     |
| `.soft-actions`  | Flex, gap 6px (boutons d'action) |
| `.text-right`    | `text-align: right`              |
| `.text-center`   | `text-align: center`             |
| `.muted`         | `color: var(--text-muted)`       |
