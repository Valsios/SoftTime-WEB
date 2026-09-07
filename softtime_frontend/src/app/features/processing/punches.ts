import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { PeriodRequest } from '../../shared/models';
import { PunchesService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-punches',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, PeriodFilter],
  template: `
    <page-header title="Pointages" subtitle="Liste et import des pointages">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="secondary" (click)="importClock()">Import pointeuse</soft-button>
      <label class="file-btn">
        <input #punchFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="accent" (click)="punchFile.click()">Import Excel</soft-button>
      </label>
    </page-header>

    <period-filter (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()"></data-table>
    </soft-card>
  `,
  styles: [`.file-btn { cursor: pointer; }`],
})
export class PunchesPage {
  private readonly svc = inject(PunchesService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);
  private lastFilter: PeriodRequest | null = null;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'date', label: 'Date', format: fmtDate },
    { key: 'heure', label: 'Heure' },
    { key: 'type', label: 'Type' },
    { key: 'importDate', label: 'Import', format: fmtDate },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.loading.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  importClock(): void {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    this.svc.importClock({
      from: this.lastFilter.from,
      to: this.lastFilter.to,
      matriculeFrom: this.lastFilter.matriculeFrom,
      matriculeTo: this.lastFilter.matriculeTo,
    }).subscribe({
      next: (r) => { this.toast.success(r.message || `${r.imported} importé(s).`); this.onSearch(this.lastFilter!); },
    });
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => { this.toast.success(r.message || `${r.imported} importé(s).`); if (this.lastFilter) this.onSearch(this.lastFilter); },
    });
    input.value = '';
  }
  exportExcel(): void {
    this.excel.download('pointages', this.columns, this.rows());
  }
}
