import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { SageDb, User } from '../../shared/models';
import { DbAccessService, SageDatabasesService, UsersService } from '../../shared/services/users.service';
import { PageHeader, SoftButton, SoftCard, SoftSelect } from '../../shared/components';

@Component({
  selector: 'app-db-access',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, SoftSelect],
  template: `
    <page-header title="Accès bases SAGE" subtitle="Bases autorisées par utilisateur">
      <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
    </page-header>

    <soft-card padding="lg">
      <soft-select
        label="Utilisateur"
        [(ngModel)]="selectedUserId"
        name="userId"
        [options]="userOptions()"
        (ngModelChange)="onUserChange($event)"
      ></soft-select>

      <div class="db-list">
        @for (db of databases(); track db.id) {
          <label class="db-item">
            <input type="checkbox" [checked]="selected().has(db.id)" (change)="toggle(db.id, $event)" />
            <span>{{ db.nomBd }} <small class="muted">({{ db.serveur }})</small></span>
          </label>
        }
      </div>
    </soft-card>
  `,
  styles: [
    `
      .db-list { margin-top: 20px; display: grid; gap: 10px; }
      .db-item { display: flex; align-items: center; gap: 10px; font-weight: 500; cursor: pointer; }
    `,
  ],
})
export class DbAccessPage implements OnInit {
  private readonly usersSvc = inject(UsersService);
  private readonly sageSvc = inject(SageDatabasesService);
  private readonly accessSvc = inject(DbAccessService);
  private readonly toast = inject(ToastService);

  readonly databases = signal<SageDb[]>([]);
  readonly userOptions = signal<{ value: number; label: string }[]>([]);
  readonly selected = signal<Set<number>>(new Set());
  readonly saving = signal(false);
  selectedUserId: number | null = null;

  ngOnInit(): void {
    this.sageSvc.list().subscribe((d) => this.databases.set(d));
    this.usersSvc.list().subscribe((users) => {
      this.userOptions.set(users.map((u) => ({ value: u.id, label: `${u.nom} (${u.login})` })));
      if (users.length) {
        this.selectedUserId = users[0].id;
        this.onUserChange(users[0].id);
      }
    });
  }

  onUserChange(userId: unknown): void {
    const id = Number(userId);
    if (!id) return;
    this.accessSvc.byUser(id).subscribe((access) => {
      this.selected.set(new Set(access.map((a) => a.sageDbId)));
    });
  }

  toggle(dbId: number, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selected.update((set) => {
      const next = new Set(set);
      if (checked) next.add(dbId);
      else next.delete(dbId);
      return next;
    });
  }

  save(): void {
    if (!this.selectedUserId) return;
    this.saving.set(true);
    this.accessSvc.replace(this.selectedUserId, [...this.selected()]).subscribe({
      next: () => { this.saving.set(false); this.toast.success('Accès enregistrés.'); },
      error: () => this.saving.set(false),
    });
  }
}
