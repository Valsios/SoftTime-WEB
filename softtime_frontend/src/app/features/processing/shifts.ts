import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { Shift } from '../../shared/models';
import { ShiftsService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-shifts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput],
  template: `
    <page-header title="Shifts" subtitle="Catalogue des shifts">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <label class="file-btn">
        <input #shiftFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="secondary" (click)="shiftFile.click()">Import Excel</soft-button>
      </label>
      <soft-button (click)="openCreate()">Ajouter</soft-button>
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <div class="soft-actions" style="justify-content: end">
            <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            <soft-button size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
          </div>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier shift' : 'Nouveau shift'" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="N° shift" type="number" [(ngModel)]="form.noShift" name="noShift"></soft-input>
        <soft-input label="Intitulé" [(ngModel)]="form.intitule" name="intitule"></soft-input>
        <soft-input label="Heure arrivée" type="time" [(ngModel)]="form.ha" name="ha"></soft-input>
        <soft-input label="Pause" type="time" [(ngModel)]="form.pause" name="pause"></soft-input>
        <soft-input label="Heure départ" type="time" [(ngModel)]="form.hd" name="hd"></soft-input>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .file-btn { cursor: pointer; }`],
})
export class ShiftsPage implements OnInit {
  private readonly svc = inject(ShiftsService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  form: Partial<Shift> = {};
  readonly columns: Column[] = [
    { key: 'noShift', label: 'N°' },
    { key: 'intitule', label: 'Intitulé' },
    { key: 'ha', label: 'Arrivée' },
    { key: 'pause', label: 'Pause' },
    { key: 'hd', label: 'Départ' },
  ];

  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({ next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  openCreate(): void { this.editId.set(null); this.form = {}; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as Shift) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as Shift;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer ce shift ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.load(); } });
  }
  exportExcel(): void {
    this.excel.download('shifts', this.columns, this.rows());
  }
  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => { this.toast.success(r.message || `${r.imported} importé(s).`); this.load(); },
    });
    input.value = '';
  }
}
