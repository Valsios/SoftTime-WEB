import { PayrollVariable, PayrollVariableSave } from '../../shared/models';

/** Variables d'heures supplémentaires, dans l'ordre historique d'écriture. */
export const HEURES_SUP = ['Exo130', 'Exo150', 'I130', 'I150', 'Ferie', 'Dimanche', 'Nuit'] as const;

/** Variables sans envoi réel (lot C). */
export const SANS_ENVOI = ['Retard', 'Absence'] as const;

/** Mention affichée à côté d'une variable non envoyée à la paie (jamais masquée). */
export const NON_ENVOYEE = 'non envoyée à la paie';

/** Clé de colonne de la page Heures supplémentaires de chaque variable. */
export const COLONNE_HEURES_SUP: Readonly<Record<string, string>> = {
  Exo130: 'exo130',
  Exo150: 'exo150',
  I130: 'i130',
  I150: 'i150',
  Ferie: 'ferie',
  Nuit: 'nuit',
  Dimanche: 'dim',
  Retard: 'retard',
  Absence: 'absence',
};

/** Comportement historique quand la configuration n'est pas lisible : les sept variables d'heures supplémentaires. */
export const ACTIVES_PAR_DEFAUT: readonly string[] = HEURES_SUP;

export function estSansEnvoi(variable: string): boolean {
  return (SANS_ENVOI as readonly string[]).includes(variable);
}

/** Cible renseignée d'une variable Retard/Absence : code de paie (SAGE) ou colonne cible (autre base). */
export function cibleRenseignee(v: PayrollVariable, baseAutre: boolean): boolean {
  const cible = baseAutre ? v.colonneCible : v.codePaie;
  return !!cible && cible.trim().length > 0;
}

/**
 * Case cochable ? Jamais si le mois en cours est déjà envoyé. Retard et Absence : seulement une fois leur cible
 * renseignée (on peut toujours les DÉcocher).
 */
export function peutCocher(v: PayrollVariable, baseAutre: boolean, moisEnvoye: boolean): boolean {
  if (moisEnvoye) return false;
  if (!estSansEnvoi(v.variable)) return true;
  return v.actif || cibleRenseignee(v, baseAutre);
}

/** Raison affichée sous une case non cochable. */
export function raisonNonCochable(v: PayrollVariable, baseAutre: boolean, moisEnvoye: boolean): string | null {
  if (moisEnvoye) return 'Mois déjà envoyé : modification impossible.';
  if (estSansEnvoi(v.variable) && !v.actif && !cibleRenseignee(v, baseAutre))
    return baseAutre ? 'Renseignez d’abord la colonne cible.' : 'Renseignez d’abord le code de paie.';
  return null;
}

/** Mêmes contrôles que le serveur (PayrollVariableRules.Valider) : messages durables affichés avant l'envoi. */
export function validerBrouillon(variables: PayrollVariable[], baseAutre: boolean): string[] {
  const erreurs: string[] = [];
  for (const v of variables) {
    if (!estSansEnvoi(v.variable)) continue;
    const unite = (v.unite ?? '').trim();
    if (unite && unite !== 'H' && unite !== 'J')
      erreurs.push(`L'unité de la variable ${v.libelle} doit être « heures » ou « jours ».`);
    if (!v.actif) continue;
    if (!cibleRenseignee(v, baseAutre))
      erreurs.push(baseAutre
        ? `La variable ${v.libelle} ne peut pas être cochée tant que sa colonne cible n'est pas renseignée.`
        : `La variable ${v.libelle} ne peut pas être cochée tant que son code de paie n'est pas renseigné.`);
    if (!unite)
      erreurs.push(`La variable ${v.libelle} ne peut pas être cochée sans unité (heures ou jours).`);
  }
  return erreurs;
}

/** Valeur comparable d'un texte facultatif (vide = absent). */
function texte(valeur: string | null | undefined): string | null {
  const t = (valeur ?? '').trim();
  return t ? t : null;
}

/** Lignes à envoyer au serveur, champs vides ramenés à null. */
export function versEnregistrement(variables: PayrollVariable[]): PayrollVariableSave[] {
  return variables.map((v) => ({
    variable: v.variable,
    actif: v.actif,
    codePaie: texte(v.codePaie),
    unite: texte(v.unite),
    colonneCible: texte(v.colonneCible),
  }));
}

/** Changements lisibles entre la configuration lue et le brouillon (texte de la confirmation). */
export function changements(initiales: PayrollVariable[], brouillon: PayrollVariable[]): string[] {
  const lignes: string[] = [];
  for (const v of brouillon) {
    const avant = initiales.find((i) => i.variable === v.variable);
    if (!avant) continue;
    if (avant.actif !== v.actif)
      lignes.push(`${v.libelle} : ${v.actif ? 'envoyée à la paie' : NON_ENVOYEE}`);
    if (texte(avant.codePaie) !== texte(v.codePaie))
      lignes.push(`${v.libelle} : code de paie ${texte(v.codePaie) ?? '(vide)'}`);
    if (texte(avant.colonneCible) !== texte(v.colonneCible))
      lignes.push(`${v.libelle} : colonne cible ${texte(v.colonneCible) ?? '(vide)'}`);
    if (texte(avant.unite) !== texte(v.unite))
      lignes.push(`${v.libelle} : unité ${uniteLibelle(v.unite)}`);
  }
  return lignes;
}

export function uniteLibelle(unite: string | null): string {
  return unite === 'H' ? 'heures' : unite === 'J' ? 'jours' : '(vide)';
}

/** Clés des colonnes de la page Heures supplémentaires dont la variable n'est pas envoyée à la paie. */
export function colonnesNonEnvoyees(actives: readonly string[]): Set<string> {
  const set = new Set<string>();
  for (const [variable, colonne] of Object.entries(COLONNE_HEURES_SUP))
    if (!actives.includes(variable)) set.add(colonne);
  return set;
}

/** Libellé d'en-tête d'une colonne : la mention est ajoutée, la colonne n'est jamais retirée. */
export function libelleColonne(label: string, envoyee: boolean): string {
  return envoyee ? label : `${label} (${NON_ENVOYEE})`;
}
