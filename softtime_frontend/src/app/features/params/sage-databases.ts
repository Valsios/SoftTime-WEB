import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SageDb } from '../../shared/models';
import { SageDatabasesService } from '../../shared/services/users.service';
import {
  Column,
  DataTable,
  PageHeader,
  SoftButton,
  SoftCard,
  SourceMappingStepper,
} from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-sage-databases',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftButton, SoftCard, DataTable, SourceMappingStepper],
  template: `
    <page-header title="Bases SAGE" subtitle="Connexions aux bases SAGE (ou toute autre base)">
      <soft-button (click)="openCreate()">Ajouter</soft-button>
    </page-header>
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

    <soft-source-mapping-stepper
      [open]="modal()"
      systemType="SAGE"
      [title]="editId() ? 'Modifier une base SAGE' : 'Nouvelle base SAGE'"
      [initial]="form()"
      [saving]="saving()"
      (closed)="closeModal()"
      (saved)="onSaved($event)"
    />
  `,
})
export class SageDatabasesPage implements OnInit {
  private readonly svc = inject(SageDatabasesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly form = signal<Partial<SageDb>>({});
  readonly columns: Column[] = [
    { key: 'id', label: 'ID' },
    { key: 'serveur', label: 'Serveur' },
    { key: 'nomBd', label: 'Base' },
    { key: 'typeBase', label: 'Type' },
    { key: 'sqlAuth', label: 'SQL Auth', format: (v) => (v ? 'Oui' : 'Non') },
  ];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (items) => {
        this.rows.set(items.map(asRow));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    this.editId.set(null);
    this.form.set({ sqlAuth: true, typeBase: 'STANDARD' });
    this.modal.set(true);
  }

  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    this.form.set({ ...(row as unknown as SageDb) });
    this.modal.set(true);
  }

  closeModal(): void {
    this.modal.set(false);
  }

  onSaved(dto: Record<string, unknown>): void {
    const id = this.editId();
    const payload = dto as unknown as SageDb;
    this.saving.set(true);
    const req = id ? this.svc.update(id, payload) : this.svc.create({ ...payload, id: 0 });
    req.subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success(
          id
            ? 'Enregistré.'
            : "Base enregistrée. Pour qu'elle apparaisse dans le menu en haut, accordez l'accès dans « Accès bases » puis reconnectez-vous.",
        );
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        this.toast.error(e?.error?.title || e?.error || 'Enregistrement impossible.');
      },
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette base SAGE ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Supprimé.');
        this.load();
      },
    });
  }
}
