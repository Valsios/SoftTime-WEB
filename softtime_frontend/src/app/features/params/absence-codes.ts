import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { AbsenceCode } from '../../shared/models';
import { AbsenceCodesService } from '../../shared/services/catalog.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftToggle } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-absence-codes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, SoftToggle],
  template: `
    <page-header title="Codes absence" subtitle="Codes absence">
      <soft-button variant="secondary" (click)="syncSage()">Sync SAGE</soft-button>
      <soft-button (click)="openCreate()">Ajouter</soft-button>
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier code' : 'Nouveau code'" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Intitulé" [(ngModel)]="form.intitule" name="intitule"></soft-input>
        <label class="toggle-row"><span>Non payé</span><soft-toggle [(ngModel)]="form.notPay" name="notPay"></soft-toggle></label>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .toggle-row { display:flex; justify-content:space-between; align-items:center; font-weight:600; }`],
})
export class AbsenceCodesPage implements OnInit {
  private readonly svc = inject(AbsenceCodesService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  form: Partial<AbsenceCode> = {};
  readonly columns: Column[] = [
    { key: 'intitule', label: 'Intitulé' },
    { key: 'notPay', label: 'Non payé', format: (v) => (v ? 'Oui' : 'Non') },
  ];

  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({ next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  openCreate(): void { this.editId.set(null); this.form = { notPay: false }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as AbsenceCode) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as AbsenceCode;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }
  syncSage(): void {
    this.svc.syncSage().subscribe({ next: () => { this.toast.success('Synchronisation SAGE terminée.'); this.load(); } });
  }
}
