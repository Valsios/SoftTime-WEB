import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PeriodRequest, PageChangeEvent } from '../../shared/models';
import { CanteenService } from '../../shared/services/reports.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, PeriodFilter } from '../../shared/components';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-canteen',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftCard, DataTable, PeriodFilter, SoftButton],
  template: `
    <page-header title="Cantine" subtitle="Repas cantine (pointages 11h–14h30)">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="secondary" [loading]="syncing()" (click)="synchronise()">
        Synchroniser (SAGE)
      </soft-button>
      <soft-button variant="danger" [loading]="busy()" (click)="purge()">Purger</soft-button>
      <soft-button [loading]="busy()" (click)="calculate()">Calculer</soft-button>
    </page-header>
    <period-filter (search)="onSearch($event)"></period-filter>

    <soft-card>
      <h2 class="section-title">Totaux cantine</h2>
      <data-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="totalsLoading()"
        [serverSide]="true"
        [total]="totalsTotal()"
        [serverPageSize]="totalsPageSize"
        [serverPage]="totalsPage()"
        [(searchTerm)]="totalsSearch"
        (searchSubmit)="onTotalsSearch($event)"
        (pageChange)="onTotalsPageChange($event)"
      ></data-table>
    </soft-card>

    <soft-card>
      <h2 class="section-title">Anomalies ({{ anomaliesTotal() }})</h2>
      <p class="muted">
        Un pointage tombe dans la fenêtre du repas un jour déclaré en absence complète (matin et
        après-midi) : le total du jour est à revoir avant synchronisation.
      </p>
      <data-table
        [columns]="anomalyColumns"
        [rows]="anomalyRows()"
        [loading]="anomaliesLoading()"
        [serverSide]="true"
        [total]="anomaliesTotal()"
        [serverPageSize]="anomaliesPageSize"
        [serverPage]="anomaliesPage()"
        [(searchTerm)]="anomalySearch"
        (searchSubmit)="onAnomaliesSearch($event)"
        (pageChange)="onAnomaliesPageChange($event)"
      ></data-table>
    </soft-card>
  `,
  styles: [`
    .section-title { font-family: var(--font-heading); font-size: 18px; font-weight: 600; color: var(--navy-950); margin: 0 0 14px; }
    .muted { color: var(--text-muted); margin: 0 0 14px; font-size: 13.5px; }
  `],
})
export class CanteenPage {
  private readonly svc = inject(CanteenService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly busy = signal(false);
  readonly syncing = signal(false);
  totalsPageSize = 50;
  anomaliesPageSize = 50;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly anomalyRows = signal<Record<string, unknown>[]>([]);
  readonly totalsLoading = signal(false);
  readonly anomaliesLoading = signal(false);
  readonly totalsPage = signal(1);
  readonly totalsTotal = signal(0);
  readonly anomaliesPage = signal(1);
  readonly anomaliesTotal = signal(0);
  readonly totalsSearch = signal('');
  readonly anomalySearch = signal('');
  private lastFilter: PeriodRequest | null = null;
  private totalsSeq = 0;
  private anomaliesSeq = 0;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateDeb', label: 'Début', format: fmtDate },
    { key: 'dateFin', label: 'Fin', format: fmtDate },
    { key: 'total', label: 'Total repas' },
  ];

  readonly anomalyColumns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'date', label: 'Date', format: fmtDate },
    { key: 'motif', label: 'Motif' },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.reloadAllFromFirstPage();
  }

  onTotalsPageChange(event: PageChangeEvent): void {
    this.totalsPage.set(event.page);
    this.totalsPageSize = event.pageSize;
    this.loadTotals();
  }

  onTotalsSearch(term: string): void {
    this.totalsSearch.set(term);
    this.totalsPage.set(1);
    this.loadTotals();
  }

  onAnomaliesPageChange(event: PageChangeEvent): void {
    this.anomaliesPage.set(event.page);
    this.anomaliesPageSize = event.pageSize;
    this.loadAnomalies();
  }

  onAnomaliesSearch(term: string): void {
    this.anomalySearch.set(term);
    this.anomaliesPage.set(1);
    this.loadAnomalies();
  }

  calculate(): void {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    this.busy.set(true);
    this.svc.compute(this.lastFilter).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success('Calcul terminé.');
        this.reloadAllFromFirstPage();
      },
      error: () => this.busy.set(false),
    });
  }

  /** Purge les cantines calculées sur la période (le calcul peut ensuite être relancé). */
  async purge(): Promise<void> {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    if (!(await this.confirm.ask(
      'Purger les cantines de cette période ? Le calcul pourra être relancé ensuite.'))) return;
    this.busy.set(true);
    this.svc.purge(this.lastFilter).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.toast.success(r.message);
        this.reloadAllFromFirstPage();
      },
      error: () => this.busy.set(false),
    });
  }

  /** Pousse les totaux de la période dans le cumul SAGE (T_CUMSAL). */
  async synchronise(): Promise<void> {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    if (!(await this.confirm.ask(
      'Pousser les totaux cantine de cette période dans la paie SAGE ?'))) return;
    this.syncing.set(true);
    this.svc.sync(this.lastFilter).subscribe({
      next: (r) => { this.syncing.set(false); this.toast.success(r.message); },
      error: () => this.syncing.set(false),
    });
  }

  exportExcel(): void {
    if (!this.lastFilter) {
      this.excel.download('cantine', this.columns, this.rows(), 'Données', this.totalsSearch());
      return;
    }
    this.svc.list(this.lastFilter).subscribe({
      next: (items) => this.excel.download('cantine', this.columns, items.map(asRow), 'Données', this.totalsSearch()),
    });
  }

  private reloadAllFromFirstPage(): void {
    this.totalsPage.set(1);
    this.anomaliesPage.set(1);
    this.loadTotals();
    this.loadAnomalies();
  }

  private loadTotals(): void {
    if (!this.lastFilter) return;
    const seq = ++this.totalsSeq;
    this.totalsLoading.set(true);
    this.svc.listPaged({
      ...this.lastFilter,
      search: this.totalsSearch(),
      page: this.totalsPage(),
      pageSize: this.totalsPageSize,
    }).subscribe({
      next: (r) => {
        if (seq !== this.totalsSeq) return;
        this.rows.set(r.items.map(asRow));
        this.totalsTotal.set(r.total);
        this.totalsPage.set(r.page);
        this.totalsLoading.set(false);
      },
      error: () => { if (seq === this.totalsSeq) this.totalsLoading.set(false); },
    });
  }

  private loadAnomalies(): void {
    if (!this.lastFilter) return;
    const seq = ++this.anomaliesSeq;
    this.anomaliesLoading.set(true);
    this.svc.anomaliesPaged({
      ...this.lastFilter,
      search: this.anomalySearch(),
      page: this.anomaliesPage(),
      pageSize: this.anomaliesPageSize,
    }).subscribe({
      next: (r) => {
        if (seq !== this.anomaliesSeq) return;
        this.anomalyRows.set(r.items.map(asRow));
        this.anomaliesTotal.set(r.total);
        this.anomaliesPage.set(r.page);
        this.anomaliesLoading.set(false);
      },
      error: () => { if (seq === this.anomaliesSeq) this.anomaliesLoading.set(false); },
    });
  }
}
