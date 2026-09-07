import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { CorrectedHour, PeriodRequest } from '../../shared/models';
import { CorrectedHoursService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-corrected-hours',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, PeriodFilter],
  template: `
    <page-header title="Heures corrigées" subtitle="Corrections manuelles">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button (click)="openCreate()">Ajouter</soft-button>
      <label class="file-btn">
        <input #corrFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="secondary" (click)="corrFile.click()">Import Excel</soft-button>
      </label>
    </page-header>

    <period-filter (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <div class="soft-actions">
            <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            <soft-button size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
          </div>
        </ng-template>
      </data-table>
    </soft-card>

    <soft-modal [open]="modal()" title="Correction" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Matricule" [(ngModel)]="form.matricule" name="matricule"></soft-input>
        <soft-input label="Date entrée" type="date" [(ngModel)]="form.dateIn" name="dateIn"></soft-input>
        <soft-input label="Date sortie" type="date" [(ngModel)]="form.dateOut" name="dateOut"></soft-input>
        <soft-input label="H. entrée" type="time" [(ngModel)]="form.hEntree" name="hEntree"></soft-input>
        <soft-input label="H. sortie" type="time" [(ngModel)]="form.hSortie" name="hSortie"></soft-input>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .file-btn { cursor: pointer; }`],
})
export class CorrectedHoursPage {
  private readonly svc = inject(CorrectedHoursService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  form: Partial<CorrectedHour> = {};
  private lastFilter: PeriodRequest | null = null;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateIn', label: 'Entrée', format: fmtDate },
    { key: 'dateOut', label: 'Sortie', format: fmtDate },
    { key: 'hEntree', label: 'H. entrée' },
    { key: 'hSortie', label: 'H. sortie' },
    { key: 'retard', label: 'Retard' },
    { key: 'hs', label: 'HS' },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.loading.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void { this.editId.set(null); this.form = {}; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    this.form = {
      ...(row as unknown as CorrectedHour),
      dateIn: fmtDate(row['dateIn']),
      dateOut: fmtDate(row['dateOut']),
    };
    this.modal.set(true);
  }

  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as CorrectedHour;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Enregistré.');
        if (this.lastFilter) this.onSearch(this.lastFilter);
      },
      error: () => this.saving.set(false),
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette correction ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Supprimé.');
        if (this.lastFilter) this.onSearch(this.lastFilter);
      },
    });
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => {
        this.toast.success(r.message || `${r.imported} importé(s).`);
        if (this.lastFilter) this.onSearch(this.lastFilter);
      },
    });
    input.value = '';
  }

  exportExcel(): void {
    this.excel.download('heures-corrigees', this.columns, this.rows());
  }
}
