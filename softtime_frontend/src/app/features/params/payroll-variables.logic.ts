import {
  PayrollTargetColumn,
  PayrollVariable,
  PayrollVariableSave,
  PayrollWriteTarget,
  PayrollWriteTargetsSave,
} from '../../shared/models';

/** Variables d'heures supplémentaires, dans l'ordre historique d'écriture. */
export const HEURES_SUP = ['Exo130', 'Exo150', 'I130', 'I150', 'Ferie', 'Dimanche', 'Nuit'] as const;

/** Variables dont le code (base SAGE) ou la colonne (autre base) est propre à la base. */
export const CODE_PROPRE = ['Retard', 'Absence'] as const;

/** Variables sans envoi réel : Absence (prochaine étape, après décision RH). */
export const SANS_ENVOI = ['Absence'] as const;

/** Unités admises par variable à code propre (la première est celle par défaut). */
export const UNITES: Readonly<Record<string, readonly string[]>> = { Retard: ['H'], Absence: ['H', 'J'] };

/** Mention affichée à côté d'une variable non envoyée à la paie (jamais masquée). */
export const NON_ENVOYEE = 'non envoyée à la paie';

/** Raison affichée sous la case d'Absence. */
export const ENVOI_ABSENCE = 'Envoi de l’absence : prochaine étape.';

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

/** Variable dont l'envoi n'existe pas encore (Absence). */
export function estSansEnvoi(variable: string): boolean {
  return (SANS_ENVOI as readonly string[]).includes(variable);
}

/** Variable dont le code ou la colonne est choisi dans la configuration de la base (Retard, Absence). */
export function aCodePropre(variable: string): boolean {
  return (CODE_PROPRE as readonly string[]).includes(variable);
}

/**
 * Destination renseignée d'une variable à code propre : code de paie (base SAGE) ; autre base : destination
 * T_PAIE_ECRITURE (Retard) ou colonne saisie (Absence).
 */
export function cibleRenseignee(v: PayrollVariable, baseAutre: boolean): boolean {
  const cible = !baseAutre ? v.codePaie : estSansEnvoi(v.variable) ? v.colonneCible : v.cibleExistante;
  return !!cible && cible.trim().length > 0;
}

/**
 * Case cochable ? Jamais si le mois en cours est déjà envoyé. Absence : jamais (une case déjà cochée peut seulement être
 * décochée). Retard : seulement une fois sa destination renseignée (on peut toujours la DÉcocher).
 */
export function peutCocher(v: PayrollVariable, baseAutre: boolean, moisEnvoye: boolean): boolean {
  if (moisEnvoye) return false;
  if (!aCodePropre(v.variable)) return true;
  if (estSansEnvoi(v.variable)) return v.actif;
  return v.actif || cibleRenseignee(v, baseAutre);
}

/** Raison affichée sous une case non cochable. */
export function raisonNonCochable(v: PayrollVariable, baseAutre: boolean, moisEnvoye: boolean): string | null {
  if (moisEnvoye) return 'Mois déjà envoyé : modification impossible.';
  if (estSansEnvoi(v.variable)) return ENVOI_ABSENCE;
  if (aCodePropre(v.variable) && !v.actif && !cibleRenseignee(v, baseAutre))
    return baseAutre
      ? 'Choisissez d’abord sa colonne dans « Destinations dans la base de paie ».'
      : 'Choisissez d’abord le code de paie.';
  return null;
}

function unitesLibelle(variable: string, separateur: string, guillemets: boolean): string {
  return (UNITES[variable] ?? ['H', 'J']).map((u) => (guillemets ? `« ${uniteLibelle(u)} »` : uniteLibelle(u))).join(separateur);
}

/** Mêmes contrôles que le serveur (PayrollVariableRules.Valider) : messages durables affichés avant l'envoi. */
export function validerBrouillon(variables: PayrollVariable[], baseAutre: boolean): string[] {
  const erreurs: string[] = [];
  for (const v of variables) {
    if (!aCodePropre(v.variable)) continue;
    const unites = UNITES[v.variable] ?? ['H', 'J'];
    const unite = (v.unite ?? '').trim();
    if (unite && !unites.includes(unite))
      erreurs.push(`L'unité de la variable ${v.libelle} doit être ${unitesLibelle(v.variable, ' ou ', true)}.`);
    if (!v.actif) continue;
    if (estSansEnvoi(v.variable)) {
      erreurs.push(`La variable ${v.libelle} ne peut pas encore être envoyée à la paie : son envoi est prévu dans une prochaine étape.`);
      continue;
    }
    if (!cibleRenseignee(v, baseAutre))
      erreurs.push(baseAutre
        ? `La variable ${v.libelle} ne peut pas être cochée tant que sa colonne cible n'est pas renseignée.`
        : `La variable ${v.libelle} ne peut pas être cochée tant que son code de paie n'est pas renseigné.`);
    // Unité unique (Retard : heures) : le serveur la prend par défaut.
    if (!unite && unites.length > 1)
      erreurs.push(`La variable ${v.libelle} ne peut pas être cochée sans unité (${unitesLibelle(v.variable, ' ou ', false)}).`);
  }
  return erreurs;
}

/** Valeur comparable d'un texte facultatif (vide = absent). */
function texte(valeur: string | null | undefined): string | null {
  const t = (valeur ?? '').trim();
  return t ? t : null;
}

/** Lignes à envoyer au serveur, champs vides ramenés à null ; une unité unique (Retard : heures) est toujours envoyée. */
export function versEnregistrement(variables: PayrollVariable[]): PayrollVariableSave[] {
  return variables.map((v) => {
    const unites = UNITES[v.variable];
    return {
      variable: v.variable,
      actif: v.actif,
      codePaie: texte(v.codePaie),
      unite: unites?.length === 1 ? unites[0] : texte(v.unite),
      colonneCible: texte(v.colonneCible),
    };
  });
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

// ---------- Destinations d'une autre base (T_PAIE_ECRITURE) ----------

/** Brouillon de l'écran des destinations. */
export interface DestinationsBrouillon {
  table: string | null;
  colonneMatricule: string | null;
  variables: PayrollWriteTarget[];
}

/** Choix proposé dans la liste des colonnes d'une variable : non utilisable = affiché mais désactivé, avec la raison. */
export interface ChoixColonne {
  nom: string;
  libelle: string;
  utilisable: boolean;
}

/**
 * Colonnes proposées pour une variable : toutes, les non numériques et (Retard) celles qui n'acceptent pas les
 * décimales étant désactivées. La valeur déjà enregistrée reste visible même si la table ne la contient plus.
 */
export function choixColonnes(colonnes: PayrollTargetColumn[], decimalesRequises: number, actuelle: string | null): ChoixColonne[] {
  const choix = colonnes.map((c) => {
    const raison = !c.numerique ? 'non numérique' : decimalesRequises > 0 && !c.accepteDecimales ? 'sans décimales' : null;
    return { nom: c.nom, libelle: raison ? `${c.nom} (${c.type}, ${raison})` : `${c.nom} (${c.type})`, utilisable: !raison };
  });
  if (actuelle && !colonnes.some((c) => c.nom.toLowerCase() === actuelle.toLowerCase()))
    choix.unshift({ nom: actuelle, libelle: `${actuelle} (introuvable dans la table)`, utilisable: false });
  return choix;
}

/** Mêmes contrôles que le serveur pour ce qui se voit à l'écran (le serveur vérifie de nouveau dans la base de paie). */
export function validerDestinations(d: DestinationsBrouillon): string[] {
  const erreurs: string[] = [];
  if (!texte(d.table)) erreurs.push('Choisissez la table de destination.');
  if (!texte(d.colonneMatricule)) erreurs.push('Choisissez la colonne matricule.');
  const parColonne = new Map<string, { colonne: string; libelles: string[] }>();
  for (const v of d.variables) {
    const colonne = texte(v.colonne);
    if (!colonne) continue;
    const groupe = parColonne.get(colonne.toLowerCase()) ?? { colonne, libelles: [] };
    groupe.libelles.push(v.libelle);
    parColonne.set(colonne.toLowerCase(), groupe);
  }
  for (const { colonne, libelles } of parColonne.values())
    if (libelles.length > 1)
      erreurs.push(`La colonne ${colonne} est choisie pour plusieurs variables (${libelles.join(', ')}).`);
  for (const v of d.variables)
    if (v.actif && !texte(v.colonne))
      erreurs.push(`La variable ${v.libelle} est envoyée à la paie : choisissez sa colonne de destination.`);
  return erreurs;
}

/** Changements lisibles entre les destinations lues et le brouillon (texte de la confirmation). */
export function changementsDestinations(initial: DestinationsBrouillon, brouillon: DestinationsBrouillon): string[] {
  const lignes: string[] = [];
  if (texte(initial.table) !== texte(brouillon.table)) lignes.push(`table ${texte(brouillon.table) ?? '(aucune)'}`);
  if (texte(initial.colonneMatricule) !== texte(brouillon.colonneMatricule))
    lignes.push(`colonne matricule ${texte(brouillon.colonneMatricule) ?? '(aucune)'}`);
  for (const v of brouillon.variables) {
    const avant = initial.variables.find((i) => i.variable === v.variable);
    if (texte(avant?.colonne) !== texte(v.colonne)) lignes.push(`${v.libelle} : colonne ${texte(v.colonne) ?? '(aucune)'}`);
  }
  return lignes;
}

/** Destinations à envoyer au serveur (champs vides ramenés à null). */
export function versDestinations(d: DestinationsBrouillon): PayrollWriteTargetsSave {
  return {
    table: texte(d.table),
    colonneMatricule: texte(d.colonneMatricule),
    variables: d.variables.map((v) => ({ variable: v.variable, colonne: texte(v.colonne) })),
  };
}
