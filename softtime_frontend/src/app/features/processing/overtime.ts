import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PeriodRequest } from '../../shared/models';
import { OvertimeService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, PeriodFilter } from '../../shared/components';
import { asRow, isMondayToSundayWeek } from '../../shared/utils/date';

@Component({
  selector: 'app-overtime',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftButton, SoftCard, DataTable, PeriodFilter],
  template: `
    <page-header title="Heures supplémentaires" subtitle="Calcul EXO/IMPO — période lundi à dimanche (7 jours)">
      <soft-button variant="danger" [loading]="busy()" (click)="purge()">Purger</soft-button>
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="accent" [loading]="busy()" (click)="syncSage()">Sync SAGE</soft-button>
      <soft-button [loading]="busy()" (click)="calculate()">Calculer</soft-button>
    </page-header>

    <period-filter [autoSearch]="true" [mondaySundayWeek]="true" (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="busy()"></data-table>
    </soft-card>
  `,
})
export class OvertimePage {
  private readonly svc = inject(OvertimeService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly busy = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);
  private lastFilter: PeriodRequest | null = null;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'exo130', label: 'EXO 130%' },
    { key: 'exo150', label: 'EXO 150%' },
    { key: 'i130', label: 'IMPO 130%' },
    { key: 'i150', label: 'IMPO 150%' },
    { key: 'ferie', label: 'Férié' },
    { key: 'nuit', label: 'Nuit' },
    { key: 'dim', label: 'Dimanche' },
    { key: 'retard', label: 'Retard' },
    { key: 'absence', label: 'Absence' },
  ];

  private requireWeek(filter: PeriodRequest | null): filter is PeriodRequest {
    if (!filter) {
      this.toast.warning('Définissez une période.');
      return false;
    }
    if (!isMondayToSundayWeek(filter.from, filter.to)) {
      this.toast.warning('La date début doit être un lundi et la date fin le dimanche de la même semaine.');
      return false;
    }
    return true;
  }

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    if (!this.requireWeek(filter)) {
      this.rows.set([]);
      return;
    }
    if (this.busy()) return;
    this.busy.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => {
        this.rows.set(items.map(asRow));
        this.busy.set(false);
      },
      error: () => this.busy.set(false),
    });
  }

  calculate(): void {
    if (this.busy() || !this.requireWeek(this.lastFilter)) return;
    this.busy.set(true);
    this.svc.calculate(this.lastFilter).subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.busy.set(false); this.toast.success('Calcul terminé.'); },
      error: () => this.busy.set(false),
    });
  }

  async purge(): Promise<void> {
    if (this.busy() || !this.requireWeek(this.lastFilter)) return;
    if (!(await this.confirm.ask('Purger les HS de la période ?'))) return;
    this.busy.set(true);
    this.svc.purge(this.lastFilter).subscribe({
      next: () => { this.busy.set(false); this.toast.success('HS purgées.'); this.rows.set([]); },
      error: () => this.busy.set(false),
    });
  }

  async syncSage(): Promise<void> {
    if (this.busy() || !this.requireWeek(this.lastFilter)) return;
    if (!(await this.confirm.ask('Pousser les HS vers SAGE (T_CUMSAL) ?'))) return;
    this.busy.set(true);
    this.svc.syncSage(this.lastFilter).subscribe({
      next: () => { this.busy.set(false); this.toast.success('Synchronisation SAGE terminée.'); },
      error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    this.excel.download('heures-supplementaires', this.columns, this.rows());
  }
}
