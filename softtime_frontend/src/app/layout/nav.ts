import { Droit } from '../core/rights';

export interface NavItem {
  label: string;
  path: string;
  icon: string;
  droit: number;
}

export interface NavSection {
  title: string;
  droit: number;
  items: NavItem[];
}

/** Left navigation, grouped by functional domain and gated by droit. */
export const NAV_SECTIONS: NavSection[] = [
  {
    title: 'Bases de données',
    droit: Droit.Databases,
    items: [
      { label: 'Bases SAGE', path: '/sage-databases', icon: 'server', droit: Droit.Databases },
      {
        label: 'Bases pointeuse',
        path: '/pointeuse-databases',
        icon: 'clock',
        droit: Droit.Databases,
      },
    ],
  },
  {
    title: 'Paramètres',
    droit: Droit.Parameters,
    items: [
      { label: 'Utilisateurs', path: '/users', icon: 'users', droit: Droit.Parameters },
      { label: 'Rôles', path: '/roles', icon: 'shield', droit: Droit.Parameters },
      { label: 'Privilèges', path: '/privileges', icon: 'key', droit: Droit.Parameters },
      { label: 'Accès bases', path: '/db-access', icon: 'lock', droit: Droit.Parameters },
      { label: 'Paramètres pointeuse', path: '/clock-params', icon: 'sliders', droit: Droit.Parameters },
      { label: 'Correspondance', path: '/correspondence', icon: 'link', droit: Droit.Parameters },
      { label: 'Correspondances paie', path: '/cardpaie', icon: 'id-card', droit: Droit.Parameters },
      { label: 'Catégories', path: '/categories', icon: 'layers', droit: Droit.Parameters },
      { label: 'Affectations', path: '/affectations', icon: 'user-check', droit: Droit.Parameters },
      { label: 'Tolérances', path: '/tolerances', icon: 'timer', droit: Droit.Parameters },
      { label: 'Jours fériés', path: '/holidays', icon: 'calendar', droit: Droit.Parameters },
      { label: 'Majorations', path: '/majorations', icon: 'percent', droit: Droit.Parameters },
      { label: 'Code Constante', path: '/code-constantes', icon: 'tag', droit: Droit.Parameters },
      { label: "Codes absence", path: '/absence-codes', icon: 'tag', droit: Droit.Parameters },
    ],
  },
  {
    title: 'Traitement',
    droit: Droit.Processing,
    items: [
      { label: 'Shifts', path: '/shifts', icon: 'grid', droit: Droit.Processing },
      { label: 'Planning salariés', path: '/employee-planning', icon: 'calendar-days', droit: Droit.Processing },
      { label: 'Pointages', path: '/punches', icon: 'fingerprint', droit: Droit.Processing },
      { label: 'Anomalies', path: '/anomalies', icon: 'alert', droit: Droit.Processing },
      { label: 'Heures corrigées', path: '/corrected-hours', icon: 'edit', droit: Droit.Processing },
      { label: 'Validation semaine', path: '/weekly-validation', icon: 'check-circle', droit: Droit.Processing },
      { label: 'Heures supp.', path: '/overtime', icon: 'trending-up', droit: Droit.Processing },
    ],
  },
  {
    title: 'États',
    droit: Droit.Reports,
    items: [{ label: 'Rapports', path: '/reports', icon: 'bar-chart', droit: Droit.Reports }],
  },
  {
    title: 'Divers',
    droit: Droit.Other,
    items: [
      { label: 'Cantine', path: '/canteen', icon: 'coffee', droit: Droit.Other },
      { label: 'Frais déplacement', path: '/travel-expenses', icon: 'map', droit: Droit.Other },
    ],
  },
  {
    title: 'Traçabilité',
    droit: Droit.Traceability,
    items: [{ label: 'Audit', path: '/audit', icon: 'history', droit: Droit.Traceability }],
  },
];
