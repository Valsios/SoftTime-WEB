import { Injectable } from '@angular/core';
import { Column } from '../components';
import { ApiService } from '../../core/api';

@Injectable({ providedIn: 'root' })
export class ExcelExportService extends ApiService {
  download(fileName: string, columns: Column[], rows: Record<string, unknown>[], sheetName = 'Données'): void {
    const headers = columns.map((c) => c.label);
    const data = rows.map((row) =>
      columns.map((c) => {
        const raw = row[c.key];
        if (c.format) return c.format(raw, row);
        return raw == null ? '' : String(raw);
      }),
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
}
