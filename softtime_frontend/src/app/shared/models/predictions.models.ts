/** Totaux d'atterrissage d'une semaine (lundi → dimanche). */
export interface WeekLandingWeek {
  semaineDebut: string;
  /** Salariés ayant un pointage ou un planning dans la semaine (0 : planning pas encore saisi). */
  salaries: number;
  /** Salariés qui finiront la semaine avec des heures supplémentaires. */
  enHeuresSup: number;
  /** Dont ceux qui passent à 150 %. */
  auDela150: number;
  /** Dont ceux qui dépassent le plafond exonéré. */
  auDelaPlafond: number;
  /** Salariés dont au moins un jour ouvré restant n'a pas de planning. */
  sansPlanning: number;
  hsAcquises: number;
  hsAtterrissage: number;
  /** Heures à retirer des plannings restants pour repasser sous les seuils. */
  heuresARetirer: number;
}

/** Atterrissage de la semaine d'un salarié, en heures. */
export interface WeekLandingRow {
  matricule: string;
  nom: string | null;
  prenom: string | null;
  departement: string | null;
  heuresContrat: number;
  /** Heures créditées sur les jours déjà pointés. */
  heuresRealisees: number;
  hsAcquises: number;
  heuresPlanifieesRestantes: number;
  joursSansPlanning: number;
  /** Heures supplémentaires si le planning restant est suivi à la lettre. */
  hsSelonPlanning: number;
  /** Écart habituel au planning appliqué aux heures restantes (positif : fait plus que prévu). */
  derive: number;
  semainesDerive: number;
  hsAtterrissage: number;
  /** 0 aucun, 1 heures supplémentaires, 2 passage à 150 %, 3 plafond exonéré dépassé. */
  niveau: number;
  seuil: number;
  heuresARetirer: number;
  /** Le seuil est déjà franchi par les heures faites : il n'est plus évitable. */
  dejaFranchi: boolean;
}

/** Atterrissage d'une semaine : totaux, fraîcheur des pointages, seuils et détail par salarié. */
export interface WeekLanding {
  totaux: WeekLandingWeek;
  aujourdhui: string;
  /** Dernier jour de pointages traités ; les jours suivants sont projetés d'après le planning. */
  dernierJourPointe: string | null;
  seuilMajoration150: number;
  plafondExonere: number;
  lignes: WeekLandingRow[];
}
