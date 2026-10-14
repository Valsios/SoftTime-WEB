/**
 * Palette et types partagés par les graphiques du tableau de bord analytique.
 *
 * Les couleurs passent par les variables CSS du thème : les graphiques suivent donc
 * automatiquement le mode clair / sombre. Aucune librairie externe n'est utilisée
 * (courbes en SVG, barres et anneau en CSS) pour rester aligné sur le reste des
 * composants maison de `shared/components`.
 */
export const CHART_COLORS = [
  '#2E427B', // Marine Softwell (navy-700)
  '#AA9E39', // Or Softwell (gold-400)
  '#047857', // Emerald-700 (Succès)
  '#BC9C22', // Or chaud (complémentaire)
  '#575D8F', // Ardoise (complémentaire)
  '#B45309', // Amber-700 (Avertissement)
  '#AEB091', // Sauge (complémentaire)
  '#BE123C', // Rose-700 (Danger)
  '#455D9C', // Navy-600
  '#819AD6', // Navy-400
  '#E1C340', // Or clair (gold-300)
  '#18244F', // Marine profond (navy-900)
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
