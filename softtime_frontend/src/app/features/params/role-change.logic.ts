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
