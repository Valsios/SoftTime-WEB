import { Droit } from '../core/rights';

export interface NavItem {
  label: string;
  path: string;
  icon: string;
  droit: number;
  permission?: string;
}

export interface NavSection {
  title: string;
  droit: number;
  items: NavItem[];
}

/** Left navigation, grouped by functional domain and gated by permissions and droit. */
export const NAV_SECTIONS: NavSection[] = [
  {
    title: 'Bases de données',
    droit: Droit.Databases,
    items: [
      {
        label: 'Bases RH / paie',
        path: '/sage-databases',
        icon: 'server',
        droit: Droit.Databases,
        permission: 'SAGE',
      },
      {
        label: 'Bases de pointage',
        path: '/pointeuse-databases',
        icon: 'clock',
        droit: Droit.Databases,
        permission: 'POINTEUSE',
      },
    ],
  },
  {
    title: 'Paramètres',
    droit: Droit.Parameters,
    items: [
      { label: 'Utilisateurs', path: '/users', icon: 'users', droit: Droit.Parameters, permission: 'PARAMS:USERS' },
      { label: 'Rôles', path: '/roles', icon: 'shield', droit: Droit.Parameters, permission: 'PARAMS:ROLES' },
      { label: 'Privilèges', path: '/privileges', icon: 'key', droit: Droit.Parameters, permission: 'PARAMS:PRIVILEGES' },
      { label: 'Accès bases', path: '/db-access', icon: 'lock', droit: Droit.Parameters, permission: 'PARAMS:DB_ACCESS' },
      { label: 'Paramètres pointeuse', path: '/clock-params', icon: 'sliders', droit: Droit.Parameters, permission: 'PARAMS:CLOCK_PARAMS' },
      { label: 'Correspondance', path: '/correspondence', icon: 'link', droit: Droit.Parameters, permission: 'PARAMS:CORRESPONDENCE' },
      { label: 'Correspondances paie', path: '/cardpaie', icon: 'id-card', droit: Droit.Parameters, permission: 'PARAMS:CARDPAIE' },
      { label: 'Catégories', path: '/categories', icon: 'layers', droit: Droit.Parameters, permission: 'PARAMS:CATEGORIES' },
      { label: 'Affectations', path: '/affectations', icon: 'user-check', droit: Droit.Parameters, permission: 'PARAMS:AFFECTATIONS' },
      { label: 'Tolérances', path: '/tolerances', icon: 'timer', droit: Droit.Parameters, permission: 'PARAMS:TOLERANCES' },
      { label: 'Jours fériés', path: '/holidays', icon: 'calendar', droit: Droit.Parameters, permission: 'PARAMS:HOLIDAYS' },
      { label: 'Majorations', path: '/majorations', icon: 'percent', droit: Droit.Parameters, permission: 'PARAMS:MAJORATIONS' },
      { label: 'Code Constante', path: '/code-constantes', icon: 'tag', droit: Droit.Parameters, permission: 'PARAMS:CODE_CONSTANTES' },
      { label: 'Codes absence', path: '/absence-codes', icon: 'tag', droit: Droit.Parameters, permission: 'PARAMS:ABSENCE_CODES' },
    ],
  },
  {
    title: 'Traitement',
    droit: Droit.Processing,
    items: [
      { label: 'Shifts', path: '/shifts', icon: 'grid', droit: Droit.Processing, permission: 'PROC:SHIFTS' },
      { label: 'Planning salariés', path: '/employee-planning', icon: 'calendar-days', droit: Droit.Processing, permission: 'PROC:EMPLOYEE_PLANNING' },
      { label: 'Pointages', path: '/punches', icon: 'fingerprint', droit: Droit.Processing, permission: 'PROC:PUNCHES' },
      { label: 'Anomalies', path: '/anomalies', icon: 'alert', droit: Droit.Processing, permission: 'PROC:ANOMALIES' },
      { label: 'Heures corrigées', path: '/corrected-hours', icon: 'edit', droit: Droit.Processing, permission: 'PROC:CORRECTED_HOURS' },
      { label: 'Validation semaine', path: '/weekly-validation', icon: 'check-circle', droit: Droit.Processing, permission: 'PROC:WEEKLY_VALIDATION' },
      { label: 'Heures supp.', path: '/overtime', icon: 'trending-up', droit: Droit.Processing, permission: 'PROC:OVERTIME' },
    ],
  },
  {
    title: 'États',
    droit: Droit.Reports,
    items: [{ label: 'Rapports', path: '/reports', icon: 'bar-chart', droit: Droit.Reports, permission: 'REPORTS:REPORTS' }],
  },
  {
    title: 'Divers',
    droit: Droit.Other,
    items: [
      { label: 'Cantine', path: '/canteen', icon: 'coffee', droit: Droit.Other, permission: 'OTHER:CANTEEN' },
      { label: 'Frais déplacement', path: '/travel-expenses', icon: 'map', droit: Droit.Other, permission: 'OTHER:TRAVEL_EXPENSES' },
    ],
  },
  {
    title: 'Traçabilité',
    droit: Droit.Traceability,
    items: [
      { label: 'Audit', path: '/audit', icon: 'history', droit: Droit.Traceability, permission: 'TRACE:AUDIT' },
      { label: 'Historique', path: '/history', icon: 'layers', droit: Droit.Traceability, permission: 'TRACE:AUDIT' },
    ],
  },
];
