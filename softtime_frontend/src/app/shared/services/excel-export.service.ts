import { Injectable, computed, signal } from '@angular/core';
import { Observable, defer, finalize } from 'rxjs';
import { Column } from '../components';
import { ApiService } from '../../core/api';
import { matchesSearch } from '../utils/search';

/**
 * Une feuille d'un classeur multi-feuilles. Les valeurs numériques restent numériques dans Excel
 * (tri, sommes) ; les libellés restent du texte.
 */
export interface ExcelSheet {
  sheetName: string;
  headers: string[];
  rows: (string | number | boolean | null)[][];
}

@Injectable({ providedIn: 'root' })
export class ExcelExportService extends ApiService {
  private readonly pending = signal(0);

  /** Vrai tant qu'un export (récupération des données ou génération du fichier) est en cours. */
  readonly exporting = computed(() => this.pending() > 0);

  /**
   * Compte une requête amont (liste à exporter) comme faisant partie de l'export, pour que le
   * bouton reste en chargement sans interruption jusqu'au téléchargement du fichier.
   */
  track<T>(source: Observable<T>): Observable<T> {
    return defer(() => {
      this.pending.update((n) => n + 1);
      return source.pipe(finalize(() => this.pending.update((n) => n - 1)));
    });
  }

  download(fileName: string, columns: Column[], rows: Record<string, unknown>[], sheetName = 'Données', search?: string): void {
    const filteredRows = search?.trim()
      ? rows.filter((row) =>
          matchesSearch(
            search,
            columns.map((column) => this.formatValue(column, row)),
          ),
        )
      : rows;
    const headers = columns.map((c) => c.label);
    const data = filteredRows.map((row) =>
      columns.map((c) => this.formatValue(c, row)),
    );
    const name = this.xlsxName(fileName);
    this.http
      .post(
        this.url('/api/v1/excel/export'),
        { fileName: name, sheetName, headers, rows: data },
        { responseType: 'blob' },
      )
      .pipe(this.counted())
      .subscribe((blob) => this.saveBlob(blob, name));
  }

  /**
   * Classeur multi-feuilles (synthèse + tableaux du tableau de bord). Le serveur ne relit aucune
   * donnée : les feuilles sont construites à partir du contenu déjà affiché.
   */
  downloadWorkbook(fileName: string, sheets: ExcelSheet[]): void {
    const name = this.xlsxName(fileName);
    this.http
      .post(
        this.url('/api/v1/excel/export-workbook'),
        { fileName: name, sheets },
        { responseType: 'blob' },
      )
      .pipe(this.counted())
      .subscribe((blob) => this.saveBlob(blob, name));
  }

  private counted<T>() {
    return (source: Observable<T>) => this.track(source);
  }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  private xlsxName(fileName: string): string {
    return fileName.endsWith('.xlsx') ? fileName : `${fileName}.xlsx`;
  }

  private formatValue(column: Column, row: Record<string, unknown>): string {
    const raw = row[column.key];
    if (column.format) return column.format(raw, row);
    return raw == null ? '' : String(raw);
  }
}
