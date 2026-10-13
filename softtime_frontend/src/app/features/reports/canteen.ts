import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { CanteenAnomaly, PeriodRequest } from '../../shared/models';
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
      <data-table [columns]="columns" [rows]="rows()" [loading]="busy()"></data-table>
    </soft-card>

    <soft-card>
      <h2 class="section-title">Anomalies ({{ anomalies().length }})</h2>
      <p class="muted">
        Un pointage tombe dans la fenêtre du repas un jour déclaré en absence complète (matin et
        après-midi) : le total du jour est à revoir avant synchronisation.
      </p>
      <data-table [columns]="anomalyColumns" [rows]="anomalyRows()" [loading]="busy()"></data-table>
    </soft-card>
  `,
  styles: [`
    .section-title { font-size: 18px; margin: 0 0 14px; }
    .muted { color: var(--soft-text-muted, #6b7280); margin: 0 0 14px; }
  `],
})
export class CanteenPage {
  private readonly svc = inject(CanteenService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly busy = signal(false);
  readonly syncing = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly anomalies = signal<CanteenAnomaly[]>([]);
  private lastFilter: PeriodRequest | null = null;

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

  readonly anomalyRows = () => this.anomalies().map(asRow);

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.load(filter);
  }

  calculate(): void {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    this.busy.set(true);
    this.svc.compute(this.lastFilter).subscribe({
      next: () => {
        this.toast.success('Calcul terminé.');
        this.load(this.lastFilter!);
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
        this.toast.success(r.message);
        this.load(this.lastFilter!);
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
    this.excel.download('cantine', this.columns, this.rows());
  }

  private load(filter: PeriodRequest): void {
    this.busy.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => {
        this.rows.set(items.map(asRow));
        this.svc.anomalies(filter).subscribe({
          next: (items) => { this.anomalies.set(items); this.busy.set(false); },
          error: () => this.busy.set(false),
        });
      },
      error: () => this.busy.set(false),
    });
  }
}
