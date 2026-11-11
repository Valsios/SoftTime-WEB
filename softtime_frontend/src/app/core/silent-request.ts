import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * Requête optionnelle de confort (liste d'un filtre, libellé d'information) : un échec n'affiche aucun toast, ne relit
 * pas les droits et ne redirige pas. Le 401 garde son traitement (rafraîchissement de session, puis déconnexion).
 * Les actions déclenchées par l'utilisateur n'utilisent jamais ce marqueur : leur refus reste affiché.
 */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

export function silentContext(): HttpContext {
  return new HttpContext().set(SILENT_ERRORS, true);
}
