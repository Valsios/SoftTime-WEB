/**
 * Palette et types partagés par les graphiques du tableau de bord analytique.
 *
 * Les couleurs passent par les variables CSS du thème : les graphiques suivent donc
 * automatiquement le mode clair / sombre. Aucune librairie externe n'est utilisée
 * (courbes en SVG, barres et anneau en CSS) pour rester aligné sur le reste des
 * composants maison de `shared/components`.
 */
export const CHART_COLORS = [
  'var(--primary-500)',
  'var(--accent-600)',
  'var(--success)',
  'var(--warning)',
  'var(--danger)',
  'var(--info)',
  // Couleurs d'appoint : au-delà de 8 séries (beaucoup de branches/départements) la palette du
  // thème s'épuise et deux secteurs voisins se retrouveraient de la même couleur.
  '#6366f1',
  '#0ea5e9',
  '#14b8a6',
  '#a855f7',
  '#f43f5e',
  '#84cc16',
  'var(--primary-800)',
  'var(--accent-900)',
];

/** Un point d'un graphique de répartition. */
export interface ChartPoint {
  label: string;
  value: number;
}

/** Une courbe : un nom, une couleur et une valeur par libellé de l'axe X. */
export interface ChartSeries {
  name: string;
  color: string;
  values: number[];
}

export type ChartFormatter = (value: number) => string;

export function colorAt(index: number): string {
  return CHART_COLORS[index % CHART_COLORS.length];
}
