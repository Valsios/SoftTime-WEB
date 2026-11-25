import { Column } from '../../shared/components';
import { fmtDate } from '../../shared/utils/date';

/** Identifiant du type de rapport « Absences justifiées » (segment d'URL de l'API : /reports/absences-justifiees). */
export const ABSENCES_JUSTIFIEES = 'absences-justifiees';

/** Mention affichée sous le choix du type : l'état ne change rien à la paie. */
export const JUSTIFIEES_NOTICE = 'État informatif : sans effet sur la paie.';

/** Message d'un état vide (après une recherche réussie qui ne renvoie aucune ligne). */
export const JUSTIFIEES_EMPTY = 'Aucune absence justifiée sur cette période.';

/**
 * Colonnes de l'état « Absences justifiées », dans l'ordre demandé. Les mêmes colonnes servent à l'écran (tableau et
 * vue en cartes) et à l'export Excel : libellés et valeurs identiques.
 */
export const JUSTIFIEES_COLUMNS: Column[] = [
  { key: 'matricule', label: 'Matricule' },
  { key: 'nom', label: 'Nom', format: (v: unknown) => String(v ?? '') },
  { key: 'prenom', label: 'Prénom', format: (v: unknown) => String(v ?? '') },
  { key: 'departement', label: 'Département', format: (v: unknown) => String(v ?? '') },
  {
    key: 'date',
    label: 'Date',
    format: (v: unknown) => (typeof v === 'string' ? fmtDate(v) : String(v ?? '')),
  },
  { key: 'motif', label: 'Motif', format: (v: unknown) => String(v ?? '') },
  {
    key: 'dureeHeures',
    label: 'Durée',
    format: (v: unknown) => (v != null && v !== '' ? `${v} h` : '-'),
  },
];
