import { RoleChangePreview } from '../../core/error.logic';

/** Un changement de rôle demande confirmation seulement s'il efface des ajustements personnels. */
export function needsConfirmation(preview: RoleChangePreview | null | undefined): boolean {
  return !!preview && preview.count > 0;
}

export function confirmationMessage(preview: RoleChangePreview): string {
  const droits = preview.count === 1 ? 'son droit personnalisé' : `ses ${preview.count} droits personnalisés`;
  return `Changer le rôle de ${preview.accountLogin} effacera ${droits}. Continuer ?`;
}

/**
 * Valeur de confirmClearAdjustments à envoyer : seulement si l'utilisateur a confirmé ET que le rôle diffère de l'original.
 */
export function confirmFlag(confirmed: boolean, originalRoleId: number | null, roleId: number | null | undefined): boolean {
  return confirmed && originalRoleId !== null && roleId !== null && roleId !== undefined && Number(roleId) !== Number(originalRoleId);
}

/** Info-bulle du champ Rôle quand on modifie son propre compte : le serveur refuse ce changement. */
export const OWN_ROLE_HINT = 'Vous ne pouvez pas modifier votre propre rôle : demandez-le à un autre administrateur.';

/** Vrai quand la fenêtre de modification porte sur le compte connecté. */
export function isEditingOwnAccount(editId: number | null | undefined, sessionUserId: number | null | undefined): boolean {
  return editId != null && sessionUserId != null && Number(editId) === Number(sessionUserId);
}

/** Aide sous le champ Rôle : son propre rôle n'est pas modifiable ; sans rôle disponible, il faut en créer un. */
export function roleFieldHint(editingSelf: boolean, roleCount: number): string {
  if (editingSelf) return OWN_ROLE_HINT;
  return roleCount ? '' : 'Aucun rôle disponible : créez un rôle avant de créer un utilisateur.';
}
