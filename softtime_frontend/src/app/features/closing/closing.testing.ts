import { of } from 'rxjs';

/** Service de clôture factice des specs de la page : aucune période envoyée (le bloc « Étape suivante » n'appelle jamais le serveur). */
export const PERIODES_FACTICES = { periods: () => of({ bddSage: 'SAGE_A', peutCloturer: true, periodes: [] }) };
