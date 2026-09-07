import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { Holiday } from '../../shared/models';
import { HolidaysService } from '../../shared/services/catalog.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-holidays',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput],
  template: `
    <page-header title="Jours fériés" subtitle="Calendrier fériés">
      <soft-button variant="secondary" (click)="importSage()">Importer SAGE</soft-button>
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
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier férié' : 'Nouveau férié'" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Intitulé" [(ngModel)]="form.intitule" name="intitule"></soft-input>
        <soft-input label="Date" type="date" [(ngModel)]="form.date" name="date"></soft-input>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; }`],
})
export class HolidaysPage implements OnInit {
  private readonly svc = inject(HolidaysService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  form: Partial<Holiday> = {};
  readonly columns: Column[] = [
    { key: 'intitule', label: 'Intitulé' },
    { key: 'date', label: 'Date', format: fmtDate },
  ];

  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({ next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  openCreate(): void { this.editId.set(null); this.form = {}; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as Holiday), date: fmtDate(row['date']) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as Holiday;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer ce jour férié ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.load(); } });
  }
  importSage(): void {
    this.svc.importSage().subscribe({ next: () => { this.toast.success('Import SAGE terminé.'); this.load(); } });
  }
}
