import { Injectable } from '@angular/core';
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
        this.url('/api/excel/export'),
        { fileName: name, sheetName, headers, rows: data },
        { responseType: 'blob' },
      )
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
        this.url('/api/excel/export-workbook'),
        { fileName: name, sheets },
        { responseType: 'blob' },
      )
      .subscribe((blob) => this.saveBlob(blob, name));
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
