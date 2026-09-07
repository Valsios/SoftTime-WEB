import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { Majoration } from '../../shared/models';
import { MajorationsService } from '../../shared/services/catalog.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-majorations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftCard, DataTable, SoftModal, SoftInput, SoftButton],
  template: `
    <page-header title="Majorations" subtitle="Taux NUIT / DIMANCHE / FERIE"></page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" title="Modifier majoration" (closed)="modal.set(false)">
      <soft-input label="Cotation" type="number" [(ngModel)]="form.cotation" name="cotation"></soft-input>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
})
export class MajorationsPage implements OnInit {
  private readonly svc = inject(MajorationsService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  form: Partial<Majoration> = {};
  readonly columns: Column[] = [
    { key: 'mojoration', label: 'Type' },
    { key: 'cotation', label: 'Cotation' },
  ];

  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({ next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as Majoration) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId()!;
    this.svc.update(id, { ...(this.form as Majoration), id }).subscribe({
      next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); },
      error: () => this.saving.set(false),
    });
  }
}
