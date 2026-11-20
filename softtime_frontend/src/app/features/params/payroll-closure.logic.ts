import { PayrollDomainState, PayrollPeriodState, PayrollSendSummary } from '../../shared/models';

/** Date ISO (aaaa-mm-jj…) en jj/mm/aaaa. */
export function dateFr(iso: string | null | undefined): string {
  const s = (iso ?? '').slice(0, 10);
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : s;
}

/** Date et heure locales d'un horodatage UTC du serveur, en jj/mm/aaaa hh:mm. */
export function dateHeureFr(iso: string | null | undefined): string {
  if (!iso) return '';
  const texte = /[zZ]$|[+-]\d{2}:\d{2}$/.test(iso) ? iso : `${iso}Z`;
  const d = new Date(texte);
  if (Number.isNaN(d.getTime())) return dateFr(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

/** Libellé d'une période. */
export function libellePeriode(p: { periodeDebut: string; periodeFin: string }): string {
  return `du ${dateFr(p.periodeDebut)} au ${dateFr(p.periodeFin)}`;
}

/** Empreinte raccourcie pour l'affichage (12 premiers caractères) ; l'empreinte complète se copie. */
export function empreinteCourte(empreinte: string | null | undefined): string {
  const e = (empreinte ?? '').trim();
  return e.length > 12 ? `${e.slice(0, 12)}…` : e;
}

export const ETAT_ENVOYEE = 'Envoyée';
export const ETAT_CLOTUREE = 'Clôturée';

export function libelleEtat(etat: PayrollPeriodState['etat']): string {
  return etat === 'CLOTUREE' ? ETAT_CLOTUREE : ETAT_ENVOYEE;
}

/** Filtre du dernier envoi, en clair (vide s'il n'y en a pas). */
export function libelleFiltre(e: PayrollSendSummary): string {
  const morceaux: string[] = [];
  if (e.departement) morceaux.push(`département ${e.departement}`);
  if (e.matriculeDe || e.matriculeA) morceaux.push(`matricules ${e.matriculeDe ?? '…'} à ${e.matriculeA ?? '…'}`);
  return morceaux.join(', ');
}

/**
 * Indication affichée sous le bouton « Clôturer » quand une condition connue manque. Le serveur reste seul juge : il
 * contrôle aussi le Contrôle avant paie et l'empreinte, et son refus est affiché tel quel.
 */
export function avertissement(p: PayrollPeriodState, aujourdhui: string): string | null {
  if (p.etat === 'CLOTUREE') return null;
  const e = p.dernierEnvoi;
  if (p.periodeFin.slice(0, 10) >= aujourdhui) return 'Période non terminée : clôture possible à partir du lendemain de sa fin.';
  if (e.statut === 'INCERTAIN') return "Dernier envoi incertain : relancez l'envoi sans filtre avant de clôturer.";
  if (e.statut !== 'SUCCES') {
    return `Dernier envoi ${e.statut === 'ECHEC' ? 'en échec' : 'inachevé'} : relancez l'envoi sans filtre avant de clôturer.`;
  }
  if (!e.instantaneId) return "Dernier envoi sans instantané : relancez l'envoi sans filtre avant de clôturer.";
  if (e.avecFiltre) return 'Dernier envoi filtré : lancez un envoi sans filtre avant de clôturer.';
  const manquants = (p.domaines ?? []).filter((d) => d.domaine !== 'HEURES' && d.etat === 'MANQUANT');
  if (manquants.length) {
    return `${manquants.map((d) => d.libelle).join(' et ')} : calculez puis envoyez sur les bornes exactes de la période, `
      + 'sans plage de matricules, avant de clôturer.';
  }
  return null;
}

/** Libellé de l'état d'un domaine dans la carte. */
export function libelleEtatDomaine(d: PayrollDomainState): string {
  switch (d.etat) {
    case 'FIGE': return 'figé';
    case 'ENVOYE': return 'envoyé';
    case 'MANQUANT': return 'manquant';
    default: return 'non exigé';
  }
}

/** Couleur du badge d'état d'un domaine. */
export function varianteDomaine(d: PayrollDomainState): 'success' | 'info' | 'warning' | 'muted' {
  switch (d.etat) {
    case 'FIGE': return 'success';
    case 'ENVOYE': return 'info';
    case 'MANQUANT': return 'warning';
    default: return 'muted';
  }
}

/** Empreintes à figer, pour la confirmation : heures, puis cantine et frais envoyés. */
export function empreintesAFiger(p: PayrollPeriodState): string {
  const autres = (p.domaines ?? [])
    .filter((d) => d.domaine !== 'HEURES' && d.etat === 'ENVOYE' && d.empreinte)
    .map((d) => `${d.libelle.toLowerCase()} ${empreinteCourte(d.empreinte)}`);
  return [`heures ${empreinteCourte(p.dernierEnvoi.empreinte)}`, ...autres].join(' ; ');
}

/** Texte de la fenêtre de confirmation : période, irréversibilité, corrections sur la période suivante. */
export function questionCloture(base: string, p: PayrollPeriodState): string {
  return `Clôturer la période ${libellePeriode(p)} de la base ${base} ? `
    + "La clôture est définitive : il n'y a pas de retour en arrière. Les valeurs envoyées en paie sont figées "
    + `(empreintes : ${empreintesAFiger(p)}) et aucune donnée de la période ne pourra plus être modifiée `
    + '(pointages, heures corrigées, anomalies, planning, validation, calcul, cantine, frais, envoi). Les corrections se feront sur la période suivante.';
}

/** Jour local au format aaaa-mm-jj. */
export function aujourdhuiIso(maintenant = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${maintenant.getFullYear()}-${p(maintenant.getMonth() + 1)}-${p(maintenant.getDate())}`;
}

/** Identifiant d'ancre de la carte « Clôture de paie (figer la période) » dans la page Bases SAGE. */
export const ANCRE_CLOTURE = 'cloture-paie';

/** Titre de la carte, distinct de la page « Contrôle avant paie ». */
export const TITRE_CARTE_CLOTURE = 'Clôture de paie (figer la période)';

/**
 * Qui voit la carte de clôture : une base active, et le droit de modifier OU de clôturer cette base (sans permissions
 * détaillées, le droit « Bases »). Partagé par la carte et par le bloc du Contrôle avant paie. Le serveur vérifie de nouveau.
 */
export function peutVoirCloture(base: string | null, permissions: readonly string[] | null | undefined, droitBases: boolean): boolean {
  if (!base) return false;
  if (!permissions || permissions.length === 0) return droitBases;
  return permissions.includes(`SAGE:${base}:UPDATE`) || permissions.includes(`SAGE:${base}:CLOTURE`);
}

export const RAPPEL_CLOTURE_BASE_ENTIERE = 'La clôture vérifie la base entière, sans filtre de département.';
export const AUCUN_ENVOI_BORNES = 'Aucun envoi sur ces bornes exactes.';

/** État d'un domaine en une expression accordée au domaine (« Heures : envoyées », « Cantine : manquante »). */
export function etatDomaineCourt(d: PayrollDomainState): string {
  const pluriel = d.domaine !== 'CANTINE';
  const base = d.etat === 'FIGE' ? 'figé' : d.etat === 'ENVOYE' ? 'envoyé' : d.etat === 'MANQUANT' ? 'manquant' : 'non exigé';
  const accorde = d.etat === 'NON_EXIGE' ? base : `${base}e${pluriel ? 's' : ''}`;
  return `${d.libelle} : ${accorde}`;
}

/** Période envoyée dont les bornes sont exactement celles du Contrôle (jours aaaa-mm-jj), sinon null. */
export function periodeCorrespondante(periodes: readonly PayrollPeriodState[], du: string, au: string): PayrollPeriodState | null {
  return periodes.find((p) => p.periodeDebut.slice(0, 10) === du && p.periodeFin.slice(0, 10) === au) ?? null;
}

export interface EtapeSuivante {
  /** Phrase d'état de la période ; vide s'il n'y a aucun envoi sur ces bornes. */
  phrase: string;
  /** Aucun envoi sur les bornes exactes. */
  aucunEnvoi: boolean;
  etat: 'AUCUN' | 'ENVOYEE' | 'CLOTUREE';
  /** Domaines exigés ou envoyés, avec leur état (ex. « Heures : envoyées »). */
  domaines: string[];
  /** Condition connue qui manque encore avant de clôturer (même texte que la carte), sinon null. */
  indication: string | null;
}

/** Résumé en lecture seule de l'état de clôture de la période du Contrôle (aucune règle de clôture dupliquée). */
export function etapeSuivante(periodes: readonly PayrollPeriodState[], du: string, au: string, aujourdhui: string): EtapeSuivante {
  const p = periodeCorrespondante(periodes, du, au);
  if (!p) return { phrase: AUCUN_ENVOI_BORNES, aucunEnvoi: true, etat: 'AUCUN', domaines: [], indication: null };
  const domaines = (p.domaines?.length ? p.domaines : [])
    .filter((d) => d.etat !== 'NON_EXIGE')
    .map(etatDomaineCourt);
  const cloturee = p.etat === 'CLOTUREE';
  const liste = domaines.length ? domaines : [cloturee ? 'Heures : figées' : 'Heures : envoyées'];
  const entete = cloturee
    ? `Période clôturée${p.cloture ? ` le ${dateHeureFr(p.cloture.clotureLe)}` : ''}.`
    : 'Période envoyée, pas encore clôturée.';
  return {
    phrase: `${entete} ${liste.join(', ')}.`,
    aucunEnvoi: false,
    etat: cloturee ? 'CLOTUREE' : 'ENVOYEE',
    domaines: liste,
    indication: cloturee ? null : avertissement(p, aujourdhui),
  };
}
