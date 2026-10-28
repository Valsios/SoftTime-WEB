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
