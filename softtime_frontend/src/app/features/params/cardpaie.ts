import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { CardPaie } from '../../shared/models';
import { CardPaieService } from '../../shared/services/catalog.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-cardpaie',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput],
  template: `
    <page-header title="Correspondances paie" subtitle="Cartes SAGE ↔ pointeuse">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <label class="file-btn">
        <input #csvFile type="file" accept=".csv,.txt" hidden (change)="onCsv($event)" />
        <soft-button variant="secondary" (click)="csvFile.click()">Import CSV</soft-button>
      </label>
      <soft-button variant="secondary" (click)="autoMap()">Auto-mapping</soft-button>
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
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier' : 'Nouvelle correspondance'" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Matricule SAGE" [(ngModel)]="form.sageMatricule" name="sageMatricule"></soft-input>
        <soft-input label="Nom SAGE" [(ngModel)]="form.sageNom" name="sageNom"></soft-input>
        <soft-input label="Prénom SAGE" [(ngModel)]="form.sagePrenom" name="sagePrenom"></soft-input>
        <soft-input label="N° pointeuse" [(ngModel)]="form.pointeuseNumero" name="pointeuseNumero"></soft-input>
        <soft-input label="Nom pointeuse" [(ngModel)]="form.pointeuseNom" name="pointeuseNom"></soft-input>
        <soft-input label="Branche" [(ngModel)]="form.branche" name="branche"></soft-input>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .file-btn { cursor: pointer; }`],
})
export class CardPaiePage implements OnInit {
  private readonly svc = inject(CardPaieService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  form: Partial<CardPaie> = {};

  readonly columns: Column[] = [
    { key: 'sageMatricule', label: 'Matricule' },
    { key: 'sageNom', label: 'Nom' },
    { key: 'sagePrenom', label: 'Prénom' },
    { key: 'pointeuseNumero', label: 'Badge' },
    { key: 'branche', label: 'Branche' },
    { key: 'date', label: 'Date', format: fmtDate },
  ];

  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({ next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  openCreate(): void { this.editId.set(null); this.form = {}; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as CardPaie) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as CardPaie;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette correspondance ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.load(); } });
  }
  autoMap(): void {
    this.svc.autoMap().subscribe({ next: (r) => this.toast.success(`${r.added} correspondance(s) ajoutée(s).`), complete: () => this.load() });
  }
  exportExcel(): void {
    this.excel.download('correspondances-paie', this.columns, this.rows());
  }
  onCsv(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importCsv(file).subscribe({
      next: (r) => { this.toast.success(r.message || `${r.imported} importé(s).`); this.load(); },
    });
    input.value = '';
  }
}
