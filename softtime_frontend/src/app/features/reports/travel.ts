import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ToastService } from '../../core/toast.service';
import { PeriodRequest } from '../../shared/models';
import { TravelService } from '../../shared/services/reports.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, PeriodFilter } from '../../shared/components';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-travel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftCard, DataTable, PeriodFilter, SoftButton],
  template: `
    <page-header title="Frais de déplacement" subtitle="Totaux à partir des pointages">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button [loading]="busy()" (click)="calculate()">Calculer</soft-button>
    </page-header>
    <period-filter (search)="onSearch($event)"></period-filter>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="busy()"></data-table>
    </soft-card>
  `,
})
export class TravelPage {
  private readonly svc = inject(TravelService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);

  readonly busy = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);
  private lastFilter: PeriodRequest | null = null;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateDeb', label: 'Début', format: fmtDate },
    { key: 'dateFin', label: 'Fin', format: fmtDate },
    { key: 'total', label: 'Total' },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.busy.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.busy.set(false); },
      error: () => this.busy.set(false),
    });
  }

  calculate(): void {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    this.busy.set(true);
    this.svc.compute(this.lastFilter).subscribe({
      next: () => {
        this.toast.success('Calcul terminé.');
        this.svc.list(this.lastFilter!).subscribe({
          next: (items) => { this.rows.set(items.map(asRow)); this.busy.set(false); },
          error: () => this.busy.set(false),
        });
      },
      error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    this.excel.download('frais-deplacement', this.columns, this.rows());
  }
}
