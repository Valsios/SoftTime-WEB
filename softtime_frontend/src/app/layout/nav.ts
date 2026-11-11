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
      { label: 'Utilisateurs', path: '/users', icon: 'users', droit: Droit.Parameters, permission: 'users.read' },
      { label: 'Rôles', path: '/roles', icon: 'shield', droit: Droit.Parameters, permission: 'roles.read' },
      { label: 'Privilèges', path: '/privileges', icon: 'key', droit: Droit.Parameters, permission: 'privileges.read' },
      { label: 'Accès bases', path: '/db-access', icon: 'lock', droit: Droit.Parameters, permission: 'db-access.read' },
      { label: 'Paramètres pointeuse', path: '/clock-params', icon: 'sliders', droit: Droit.Parameters, permission: 'clock-params.manage' },
      { label: 'Correspondance', path: '/correspondence', icon: 'link', droit: Droit.Parameters, permission: 'correspondence.manage' },
      { label: 'Correspondances paie', path: '/cardpaie', icon: 'id-card', droit: Droit.Parameters, permission: 'card-paie.read' },
      { label: 'Catégories', path: '/categories', icon: 'layers', droit: Droit.Parameters, permission: 'categories.read' },
      { label: 'Affectations', path: '/affectations', icon: 'user-check', droit: Droit.Parameters, permission: 'affectations.read' },
      { label: 'Tolérances', path: '/tolerances', icon: 'timer', droit: Droit.Parameters, permission: 'tolerances.read' },
      { label: 'Jours fériés', path: '/holidays', icon: 'calendar', droit: Droit.Parameters, permission: 'holidays.read' },
      { label: 'Majorations', path: '/majorations', icon: 'percent', droit: Droit.Parameters, permission: 'majorations.read' },
      { label: 'Code Constante', path: '/code-constantes', icon: 'tag', droit: Droit.Parameters, permission: 'code-constantes.manage' },
      { label: 'Codes absence', path: '/absence-codes', icon: 'tag', droit: Droit.Parameters, permission: 'absence-codes.read' },
    ],
  },
  {
    title: 'Traitement',
    droit: Droit.Processing,
    items: [
      { label: 'Shifts', path: '/shifts', icon: 'grid', droit: Droit.Processing, permission: 'shifts.read' },
      { label: 'Planning salariés', path: '/employee-planning', icon: 'calendar-days', droit: Droit.Processing, permission: 'employee-planning.read' },
      { label: 'Pointages', path: '/punches', icon: 'fingerprint', droit: Droit.Processing, permission: 'punches.manage' },
      { label: 'Anomalies', path: '/anomalies', icon: 'alert', droit: Droit.Processing, permission: 'anomalies.manage' },
      { label: 'Heures corrigées', path: '/corrected-hours', icon: 'edit', droit: Droit.Processing, permission: 'corrected-hours.read' },
      { label: 'Validation semaine', path: '/weekly-validation', icon: 'check-circle', droit: Droit.Processing, permission: 'weekly-validation.manage' },
      { label: 'Heures supp.', path: '/overtime', icon: 'trending-up', droit: Droit.Processing, permission: 'overtime.manage' },
    ],
  },
  {
    title: 'États',
    droit: Droit.Reports,
    items: [
      { label: 'Rapports', path: '/reports', icon: 'bar-chart', droit: Droit.Reports, permission: 'reports.read' },
      {
        label: 'Prévisions',
        path: '/predictions',
        icon: 'trending-up',
        droit: Droit.Reports,
        permission: 'predictions.read',
      },
      {
        label: 'Contrôle avant paie',
        path: '/closing',
        icon: 'check-circle',
        droit: Droit.Reports,
        permission: 'closing.read',
      },
    ],
  },
  {
    title: 'Divers',
    droit: Droit.Other,
    items: [
      { label: 'Cantine', path: '/canteen', icon: 'coffee', droit: Droit.Other, permission: 'canteen.manage' },
      { label: 'Frais déplacement', path: '/travel-expenses', icon: 'map', droit: Droit.Other, permission: 'travel-expenses.manage' },
    ],
  },
  {
    title: 'Traçabilité',
    droit: Droit.Traceability,
    items: [
      { label: 'Audit', path: '/audit', icon: 'history', droit: Droit.Traceability, permission: 'audit.read' },
      { label: 'Historique', path: '/history', icon: 'layers', droit: Droit.Traceability, permission: 'audit.read' },
    ],
  },
];
