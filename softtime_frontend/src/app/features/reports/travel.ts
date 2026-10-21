import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
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
      <soft-button variant="secondary" [loading]="syncing()" (click)="synchronise()">
        Synchroniser (SAGE)
      </soft-button>
      <soft-button [loading]="busy()" (click)="calculate()">Calculer</soft-button>
    </page-header>
    <period-filter (search)="onSearch($event)"></period-filter>
    <soft-card>
      <data-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [serverSide]="true"
        [total]="total()"
        [pageSize]="PAGE_SIZE"
        [currentPage]="page()"
        (pageChange)="onPageChange($event)"
      ></data-table>
    </soft-card>
  `,
})
export class TravelPage {
  private readonly svc = inject(TravelService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly busy = signal(false);
  readonly loading = signal(false);
  readonly syncing = signal(false);
  readonly PAGE_SIZE = 50;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  private lastFilter: PeriodRequest | null = null;
  private requestSeq = 0;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateDeb', label: 'Début', format: fmtDate },
    { key: 'dateFin', label: 'Fin', format: fmtDate },
    { key: 'total', label: 'Total' },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.page.set(1);
    this.loadPage();
  }

  onPageChange(page: number): void {
    this.page.set(page);
    this.loadPage();
  }

  private loadPage(): void {
    if (!this.lastFilter) return;
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged({ ...this.lastFilter, page: this.page(), pageSize: this.PAGE_SIZE }).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        this.rows.set(r.items.map(asRow));
        this.total.set(r.total);
        this.page.set(r.page);
        this.loading.set(false);
      },
      error: () => { if (seq === this.requestSeq) this.loading.set(false); },
    });
  }

  calculate(): void {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    this.busy.set(true);
    this.svc.compute(this.lastFilter).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success('Calcul terminé.');
        this.page.set(1);
        this.loadPage();
      },
      error: () => this.busy.set(false),
    });
  }

  /** Pousse les totaux de la période dans le cumul SAGE (T_CUMSAL). */
  async synchronise(): Promise<void> {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    if (!(await this.confirm.ask(
      'Pousser les totaux de frais de déplacement de cette période dans la paie SAGE ?'))) return;
    this.syncing.set(true);
    this.svc.sync(this.lastFilter).subscribe({
      next: (r) => { this.syncing.set(false); this.toast.success(r.message); },
      error: () => this.syncing.set(false),
    });
  }

  exportExcel(): void {
    if (!this.lastFilter) {
      this.excel.download('frais-deplacement', this.columns, this.rows());
      return;
    }
    this.svc.list(this.lastFilter).subscribe({
      next: (items) => this.excel.download('frais-deplacement', this.columns, items.map(asRow)),
    });
  }
}
