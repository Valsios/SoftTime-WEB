import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PeriodRequest } from '../../shared/models';
import { AnomaliesService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-anomalies',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftButton, SoftCard, DataTable, PeriodFilter],
  template: `
    <page-header title="Anomalies" subtitle="Heures en anomalie">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
    </page-header>
    <period-filter (search)="onSearch($event)"></period-filter>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <soft-button size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
        </ng-template>
      </data-table>
    </soft-card>
  `,
})
export class AnomaliesPage {
  private readonly svc = inject(AnomaliesService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateIn', label: 'Entrée', format: fmtDate },
    { key: 'dateOut', label: 'Sortie', format: fmtDate },
    { key: 'hEntree', label: 'H. entrée' },
    { key: 'hSortie', label: 'H. sortie' },
    { key: 'intituleAbsence', label: 'Absence' },
  ];

  onSearch(filter: PeriodRequest): void {
    this.loading.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette anomalie ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.rows.update((r) => r.filter((x) => x['id'] !== row['id'])); } });
  }

  exportExcel(): void {
    this.excel.download('anomalies', this.columns, this.rows());
  }
}
