/**
 * Visibilité des menus et des routes, alignée sur le serveur : un élément n'est accessible que si
 * (a) la permission fine l'autorise ET (b) la porte que son contrôleur exige est présente dans le jeton.
 * Logique pure, sans Angular, testée par access.logic.spec.ts.
 */

export const ADMIN_ROLE_ID = 1;

export interface AccessContext {
  roleId: number | null | undefined;
  /** Permissions fines effectives (liste renvoyée par le serveur à la connexion). */
  permissions: readonly string[];
  /** Vrai si le compte a des écarts personnels (ou une liste de permissions non vide : voir permissionAllows). */
  customized: boolean;
  /** Portes du compte lues dans le jeton (claim « rights »), ou null si le jeton est illisible. */
  gates: readonly number[] | null;
  /** Portes renvoyées à la connexion : repli si le jeton est illisible. */
  fallbackRights: readonly number[];
}

export interface NavItemLike {
  droit: number;
  permission?: string;
}

export interface NavSectionLike<I extends NavItemLike = NavItemLike> {
  droit: number;
  items: I[];
}

/** Portes du claim « rights » d'un jeton JWT (sans vérifier la signature : le serveur la vérifie). */
export function decodeTokenGates(token: string | null | undefined): number[] | null {
  if (!token) return null;
  const parts = token.split('.');
  if (parts.length < 2) return null;
  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    );
    const claim = (JSON.parse(json) as { rights?: unknown }).rights;
    if (claim === undefined || claim === null || claim === '') return [];
    const list = Array.isArray(claim) ? claim : String(claim).split(',');
    return list.map((v) => Number(String(v).trim())).filter((n) => Number.isInteger(n) && n > 0);
  } catch {
    return null;
  }
}

export function isAdministrator(ctx: AccessContext): boolean {
  return ctx.roleId === ADMIN_ROLE_ID;
}

/** Porte serveur présente (l'administrateur les a toutes). Sans porte demandée, toujours vrai. */
export function gateAllows(ctx: AccessContext, droit?: number): boolean {
  if (isAdministrator(ctx)) return true;
  if (!droit) return true;
  return (ctx.gates ?? ctx.fallbackRights).includes(droit);
}

/** Permission fine seule (logique historique de SessionStore.hasPermission, sans la porte). */
export function permissionAllows(ctx: AccessContext, code?: string, fallbackDroit?: number): boolean {
  if (isAdministrator(ctx)) return true;
  const gateFallback = () => (fallbackDroit ? gateAllows(ctx, fallbackDroit) : true);
  if (!code) return gateFallback();
  if (ctx.customized || ctx.permissions.length > 0) {
    if (code === 'SAGE') return ctx.permissions.some((p) => p.startsWith('sage.'));
    if (code === 'POINTEUSE') return ctx.permissions.some((p) => p.startsWith('pointeuse.'));
    return ctx.permissions.includes(code);
  }
  return gateFallback();
}

/** Accès complet : permission fine ET porte serveur. */
export function canAccess(ctx: AccessContext, code?: string, droit?: number): boolean {
  return permissionAllows(ctx, code, droit) && gateAllows(ctx, droit);
}

/** Sections de menu visibles : éléments accessibles seulement, section vide masquée. */
export function visibleSections<I extends NavItemLike, S extends NavSectionLike<I>>(
  sections: readonly S[],
  ctx: AccessContext,
): S[] {
  return sections
    .map((section) => ({ ...section, items: section.items.filter((item) => canAccess(ctx, item.permission, item.droit)) }))
    .filter((section) => section.items.length > 0);
}

/** Au moins une base SAGE utilisable : sinon les pages qui lisent une base affichent l'état vide. */
export function hasUsableDatabase(databases: readonly { nomBd?: string | null }[] | null | undefined): boolean {
  return (databases ?? []).some((d) => !!d.nomBd);
}

export const NO_DATABASE_MESSAGE =
  "Aucune base n'est disponible pour votre compte. Contactez un administrateur pour obtenir l'accès à une base.";

/** Type de base des droits par base : « sage » (RH / paie) ou « pointeuse ». */
export type DatabaseKind = 'sage' | 'pointeuse';

/** Action sur une base : la lire (l'utiliser), la modifier, la supprimer. */
export type DatabaseAction = 'read' | 'update' | 'delete';

export const DATABASE_ACTIONS: readonly DatabaseAction[] = ['read', 'update', 'delete'];

/** Code d'un droit par base : par identifiant (« sage.12.read ») ou pour toutes les bases, présentes et futures (« sage.all.read »). */
export function databaseCode(kind: DatabaseKind, target: number | 'all', action: DatabaseAction): string {
  return `${kind}.${target}.${action}`;
}

/** Vrai si les permissions accordent l'action sur la base, par son identifiant ou par le joker « all ». */
export function grantsDatabase(
  permissions: readonly string[],
  kind: DatabaseKind,
  id: number,
  action: DatabaseAction,
): boolean {
  return permissions.includes(databaseCode(kind, id, action)) || permissions.includes(databaseCode(kind, 'all', action));
}
