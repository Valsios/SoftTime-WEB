import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { Role, User } from '../../shared/models';
import { CardPaieService } from '../../shared/services/catalog.service';
import { RolesService, UsersService } from '../../shared/services/users.service';
import {
  Column,
  DataTable,
  PageHeader,
  SoftButton,
  SoftCard,
  SoftInput,
  SoftModal,
  SoftSelect,
} from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-users',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect],
  template: `
    <page-header title="Utilisateurs" subtitle="Gestion RESPONSABLE">
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
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier utilisateur' : 'Nouvel utilisateur'" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Nom" [(ngModel)]="form.nom" name="nom"></soft-input>
        <soft-input label="Login" [(ngModel)]="form.login" name="login"></soft-input>
        <soft-select label="Matricule" [(ngModel)]="form.matricule" name="matricule" placeholder="Aucun" [options]="matriculeOptions()"></soft-select>
        <soft-input label="Mot de passe" type="password" [(ngModel)]="form.password" name="password"></soft-input>
        <soft-select label="Rôle" [(ngModel)]="form.roleId" name="roleId" [options]="roleOptions()"></soft-select>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; }`],
})
export class UsersPage implements OnInit {
  private readonly svc = inject(UsersService);
  private readonly rolesSvc = inject(RolesService);
  private readonly cards = inject(CardPaieService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly roleOptions = signal<{ value: number; label: string }[]>([]);
  readonly matriculeOptions = signal<{ value: string; label: string }[]>([]);
  form: Partial<User> = {};

  readonly columns: Column[] = [
    { key: 'id', label: 'ID' },
    { key: 'nom', label: 'Nom' },
    { key: 'login', label: 'Login' },
    { key: 'matricule', label: 'Matricule' },
    { key: 'roleName', label: 'Rôle' },
  ];

  ngOnInit(): void {
    this.rolesSvc.list().subscribe((roles) =>
      this.roleOptions.set(roles.map((r) => ({ value: r.id, label: r.nom }))),
    );
    this.cards.list().subscribe({
      next: (list) =>
        this.matriculeOptions.set(
          [...new Set(list.map((c) => c.sageMatricule).filter((m): m is string => !!m))]
            .sort((a, b) => a.localeCompare(b, 'fr', { numeric: true }))
            .map((m) => ({ value: m, label: m })),
        ),
      error: () => undefined,
    });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (items) => {
        const roles = this.roleOptions();
        this.rows.set(
          items.map((u) =>
            asRow({
              ...u,
              roleName: roles.find((r) => Number(r.value) === Number(u.roleId))?.label ?? String(u.roleId ?? ''),
            }),
          ),
        );
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void { this.editId.set(null); this.form = { roleId: this.roleOptions()[0]?.value ?? 0 }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as User) }; this.modal.set(true); }

  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as User;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cet utilisateur ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.load(); } });
  }
}
