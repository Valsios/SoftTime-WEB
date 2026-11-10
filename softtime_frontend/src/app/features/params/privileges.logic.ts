/** Identifiant du rôle administrateur, géré par le serveur. */
export const ADMIN_ROLE_ID = 1;

/** Vrai si l'identifiant de rôle est celui de l'administrateur. */
export function isAdminRole(roleId: number | null | undefined): boolean {
  return roleId === ADMIN_ROLE_ID;
}

/** Seul un administrateur peut modifier les droits d'un rôle autre que le sien. */
export function canEditRolePermissions(
  sessionRoleId: number | null | undefined,
  targetRoleId: number | null | undefined,
): boolean {
  return isAdminRole(sessionRoleId) && targetRoleId !== null && targetRoleId !== undefined && !isAdminRole(targetRoleId);
}

/** Nombre d'utilisateurs rattachés à un rôle dans la liste fournie. */
export function countUsersOfRole(users: { roleId: number }[], roleId: number): number {
  return users.filter((user) => user.roleId === roleId).length;
}

/** Message d'impact affiché avant l'enregistrement des droits d'un rôle (V4). */
export function buildRoleImpactMessage(n: number): string {
  return `Ce rôle est attribué à ${n} utilisateur(s). Les utilisateurs dont les droits sont personnalisés ne sont pas concernés. Les autres verront le changement à leur prochaine connexion (12 h maximum).`;
}

/** Extrait le message d'erreur renvoyé par le serveur, avec un libellé de repli. */
export function extractErrorMessage(err: unknown): string {
  const fallback = 'Enregistrement refusé par le serveur.';
  if (typeof err === 'string') {
    return err.trim() ? err : fallback;
  }
  if (!err || typeof err !== 'object') return fallback;

  const error = (err as { error?: unknown; message?: unknown }).error;
  if (typeof error === 'string') {
    return error.trim() ? error : fallback;
  }
  if (error && typeof error === 'object') {
    const inner = error as { message?: unknown; title?: unknown };
    if (typeof inner.message === 'string' && inner.message.trim()) return inner.message;
    if (typeof inner.title === 'string' && inner.title.trim()) return inner.title;
  }

  const message = (err as { message?: unknown }).message;
  if (typeof message === 'string' && message.trim()) return message;
  return fallback;
}

/**
 * Message d'erreur de l'écran Privilèges, sans texte technique : les refus du serveur sont reformulés avec la marche à
 * suivre ; tout autre message est affiché tel quel.
 */
export function readablePrivilegesError(err: unknown): string {
  const message = extractErrorMessage(err);
  if (message.startsWith('Codes de permission inconnus')) {
    const codes = message.slice(message.indexOf(':') + 1).trim().replace(/\.$/, '');
    return `Droits non reconnus par le serveur (${codes}). Rechargez la page : la liste des droits a peut-être changé.`;
  }
  if (message.startsWith('Code de permission trop long')) {
    return 'Un droit porte un nom trop long (128 caractères au plus) : raccourcissez le nom de la base concernée.';
  }
  if (message.includes('réservée aux administrateurs')) {
    return 'Seul un administrateur peut modifier les privilèges.';
  }
  return message;
}

/** Confirmation avant de retirer des droits à son propre compte (ils s'appliquent dès la requête suivante). */
export function selfRightsRemovalMessage(removedCount: number): string {
  return `Vous allez retirer ${removedCount} droit(s) à votre propre compte. Ils ne seront plus accordés dès votre prochaine action, `
    + "et vous pourriez perdre l'accès à cet écran. Continuer ?";
}

/** Nombre de droits par source (droits effectifs du serveur) : hérités du rôle, ajoutés, retirés. */
export function countBySource(rights: readonly { source: string; isEffective: boolean }[]): { role: number; added: number; removed: number } {
  return {
    role: rights.filter((r) => r.source === 'ROLE' && r.isEffective).length,
    added: rights.filter((r) => r.source === 'ADDED' && r.isEffective).length,
    removed: rights.filter((r) => r.source === 'REMOVED').length,
  };
}

/** Confirmation avant de retirer un ancien code de base sans action. */
export function legacyRemovalMessage(code: string): string {
  return `Retirer l'ancien code « ${code} » ? Il donne encore la porte Bases à ce compte, sans droit sur une base précise. `
    + 'Retirez-le seulement si ce compte n\'a pas besoin de la porte Bases.';
}

/** Calcule les différences entre la base de référence et les droits de travail. */
export function diffPermissions(
  baseline: Iterable<string>,
  working: Iterable<string>,
): { added: string[]; removed: string[]; count: number } {
  const baseSet = new Set(baseline);
  const workSet = new Set(working);
  const added: string[] = [];
  const removed: string[] = [];

  for (const permission of workSet) {
    if (!baseSet.has(permission)) {
      added.push(permission);
    }
  }
  for (const permission of baseSet) {
    if (!workSet.has(permission)) {
      removed.push(permission);
    }
  }

  added.sort();
  removed.sort();

  return {
    added,
    removed,
    count: added.length + removed.length,
  };
}

/** Résout le nom d'un rôle dans la liste chargée, avec repli « Rôle #<id> » si introuvable. */
export function resolveRoleLabel(
  roles: { id: number; nom: string }[],
  roleId: number | null | undefined,
): string {
  if (roleId === null || roleId === undefined) return '—';
  const role = roles.find((r) => r.id === roleId);
  return role?.nom?.trim() ? role.nom : `Rôle #${roleId}`;
}

/** Écarts d'un compte par rapport aux droits de son rôle. */
export interface PermissionSources {
  /** Codes accordés au compte mais pas à son rôle. */
  added: Set<string>;
  /** Codes du rôle absents du compte. */
  removed: Set<string>;
  count: number;
}

/** Marqueurs internes du stockage (par exemple « aucun droit ») : jamais des droits. */
export function isInternalMarker(code: string): boolean {
  return code.startsWith('__SOFTTIME_EMPTY_');
}

export function computeSources(rolePermissions: Iterable<string>, userPermissions: Iterable<string>): PermissionSources {
  const role = new Set([...rolePermissions].filter((c) => !isInternalMarker(c)));
  const user = new Set([...userPermissions].filter((c) => !isInternalMarker(c)));
  const added = new Set([...user].filter((c) => !role.has(c)));
  const removed = new Set([...role].filter((c) => !user.has(c)));
  return { added, removed, count: added.size + removed.size };
}

/** Libellé du badge d'un compte personnalisé : « Personnalisé · +A −R ». */
export function customizedBadgeLabel(sources: PermissionSources): string {
  return `Personnalisé · +${sources.added.size} \u2212${sources.removed.size}`;
}

export function buildResetToRoleMessage(roleName: string, adjustments: number, hasUnsavedChanges: boolean): string {
  const effaces = adjustments === 1 ? 'Le 1 ajustement personnel sera effacé.' : `Les ${adjustments} ajustements personnels seront effacés.`;
  const base = `Revenir aux droits du rôle ${roleName} ? ${effaces}`;
  return hasUnsavedChanges ? `${base} Les modifications non enregistrées sur ce compte seront aussi perdues.` : base;
}

/** Suite du message de succès après enregistrement : les droits ne changent que lorsque le jeton est renouvelé. */
export function appliesAtNextLoginNote(mode: 'user' | 'role'): string {
  const who = mode === 'user' ? 'de la personne' : 'des personnes de ce rôle';
  return `Les changements s'appliquent à la prochaine connexion ${who} (jusqu'à 12 h).`;
}
