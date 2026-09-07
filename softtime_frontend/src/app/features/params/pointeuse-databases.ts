import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PointeuseDb } from '../../shared/models';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
import {
  Column,
  DataTable,
  PageHeader,
  SoftButton,
  SoftCard,
  SoftInput,
  SoftModal,
  SoftToggle,
} from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-pointeuse-databases',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    PageHeader,
    SoftButton,
    SoftCard,
    DataTable,
    SoftModal,
    SoftInput,
    SoftToggle,
  ],
  template: `
    <page-header title="Bases pointeuse" subtitle="Connexions aux bases pointeuse">
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

    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier' : 'Nouvelle base pointeuse'" (closed)="closeModal()">
      <form class="form-grid">
        <soft-input label="Serveur" [(ngModel)]="form.serveur" name="serveur"></soft-input>
        <soft-input label="Nom base" [(ngModel)]="form.nomBd" name="nomBd"></soft-input>
        <soft-input label="Type pointage" [(ngModel)]="form.typePointage" name="typePointage"></soft-input>
        <soft-input label="Login SQL" [(ngModel)]="form.login" name="login"></soft-input>
        <soft-input label="Mot de passe" type="password" [(ngModel)]="form.password" name="password"></soft-input>
        <label class="toggle-row"><span>SQL Auth</span><soft-toggle [(ngModel)]="form.sqlAuth" name="sqlAuth"></soft-toggle></label>
        <label class="toggle-row"><span>Active</span><soft-toggle [(ngModel)]="form.active" name="active"></soft-toggle></label>
      </form>
      <div footer>
        <soft-button variant="ghost" (click)="closeModal()">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [` .form-grid { display: grid; gap: 14px; } .toggle-row { display:flex; justify-content:space-between; align-items:center; font-weight:600; } `],
})
export class PointeuseDatabasesPage implements OnInit {
  private readonly svc = inject(PointeuseDatabasesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  form: Partial<PointeuseDb> = {};

  readonly columns: Column[] = [
    { key: 'id', label: 'ID' },
    { key: 'serveur', label: 'Serveur' },
    { key: 'nomBd', label: 'Base' },
    { key: 'typePointage', label: 'Type' },
    { key: 'active', label: 'Active', format: (v) => (v ? 'Oui' : 'Non') },
  ];

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void { this.editId.set(null); this.form = { sqlAuth: true, active: true }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as PointeuseDb) }; this.modal.set(true); }
  closeModal(): void { this.modal.set(false); }

  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as PointeuseDb;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette base pointeuse ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.load(); } });
  }
}
