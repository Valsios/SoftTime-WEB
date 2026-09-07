import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PeriodRequest } from '../../shared/models';
import { WeeklyValidationService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-weekly-validation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftButton, SoftCard, DataTable, PeriodFilter],
  template: `
    <page-header title="Validation semaine" subtitle="Calcul HT/HS lundi–dimanche — seules les lignes cochées sont validées">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="secondary" [loading]="busy()" (click)="validate(false)">Annuler validation</soft-button>
      <soft-button [loading]="busy()" (click)="validate(true)">Valider semaine</soft-button>
    </page-header>

    <period-filter searchLabel="Calculer" (search)="preview($event)"></period-filter>

    <soft-card>
      <data-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [selectable]="true"
        trackKey="matricule"
        [(selectedKeys)]="selectedKeys"
      ></data-table>
    </soft-card>
  `,
})
export class WeeklyValidationPage {
  private readonly svc = inject(WeeklyValidationService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly selectedKeys = signal<string[]>([]);
  private lastFilter: PeriodRequest | null = null;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateDeb', label: 'Début', format: fmtDate },
    { key: 'dateFin', label: 'Fin', format: fmtDate },
    { key: 'ht', label: 'HT' },
    { key: 'hs', label: 'HS' },
    { key: 'retard', label: 'Retard' },
    { key: 'absence', label: 'Absence' },
    { key: 'validated', label: 'Validé', format: (v) => (v ? 'Oui' : 'Non') },
  ];

  preview(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.loading.set(true);
    this.svc.preview(filter).subscribe({
      next: (items) => {
        this.rows.set(items.filter((i) => !i.validated).map(asRow));
        this.selectedKeys.set([]);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  async validate(ok: boolean): Promise<void> {
    if (!this.lastFilter) {
      this.toast.warning("Lancez d'abord un aperçu.");
      return;
    }
    const selectedMatricules = this.selectedKeys();
    if (selectedMatricules.length === 0) {
      this.toast.warning('Cochez au moins une ligne.');
      return;
    }
    const msg = ok
      ? `Valider la semaine pour ${selectedMatricules.length} salarié(s) sélectionné(s) ?`
      : `Annuler la validation pour ${selectedMatricules.length} salarié(s) sélectionné(s) ?`;
    if (!(await this.confirm.ask(msg))) return;
    this.busy.set(true);
    const body = { ...this.lastFilter, selectedMatricules };
    const req = ok ? this.svc.validate(body) : this.svc.unvalidate(body);
    req.subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(ok ? 'Lignes sélectionnées validées.' : 'Validation annulée pour la sélection.');
        this.preview(this.lastFilter!);
      },
      error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    this.excel.download('validation-semaine', this.columns, this.rows());
  }
}
