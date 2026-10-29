/** Colonne attendue par un import, telle qu'affichée dans l'aide « Modèles d'import ». */
export interface ImportTemplateColumn {
  header: string;
  required: boolean;
  format: string;
  example: string;
  note?: string | null;
}

/** Modèle d'import : matrice des colonnes, exemple de fichier et règles. */
export interface ImportTemplate {
  key: string;
  label: string;
  /** `xlsx` ou `csv` : détermine le fichier téléchargeable. */
  format: string;
  fileName: string;
  description: string;
  columns: ImportTemplateColumn[];
  exampleRows: string[][];
  notes: string[];
}
