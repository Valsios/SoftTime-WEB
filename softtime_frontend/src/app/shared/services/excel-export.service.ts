import { Injectable } from '@angular/core';
import { Column } from '../components';
import { ApiService } from '../../core/api';
import { matchesSearch } from '../utils/search';

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
    this.http
      .post(
        this.url('/api/excel/export'),
        { fileName, sheetName, headers, rows: data },
        { responseType: 'blob' },
      )
      .subscribe((blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName.endsWith('.xlsx') ? fileName : `${fileName}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
      });
  }

  private formatValue(column: Column, row: Record<string, unknown>): string {
    const raw = row[column.key];
    if (column.format) return column.format(raw, row);
    return raw == null ? '' : String(raw);
  }
}
