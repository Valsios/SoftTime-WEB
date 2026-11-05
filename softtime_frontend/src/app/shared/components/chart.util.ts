/**
 * Palette et types partagés par les graphiques du tableau de bord analytique.
 *
 * Les couleurs passent par les variables CSS du thème : les graphiques suivent donc
 * automatiquement le mode clair / sombre. Aucune librairie externe n'est utilisée
 * (courbes en SVG, barres et anneau en CSS) pour rester aligné sur le reste des
 * composants maison de `shared/components`.
 */
export const CHART_COLORS = [
  'var(--chart-1)',
  'var(--chart-2)',
  'var(--chart-3)',
  'var(--chart-4)',
  'var(--chart-5)',
  'var(--chart-6)',
  'var(--chart-7)',
  'var(--chart-8)',
  'var(--chart-9)',
  'var(--chart-10)',
  'var(--chart-11)',
  'var(--chart-12)',
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
