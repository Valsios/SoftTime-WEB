import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { SageDb } from '../../shared/models';
import { SageDatabasesService } from '../../shared/services/users.service';
import { AuthService } from '../../shared/services/auth.service';
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
      @if (canCreate()) {
        <soft-button (click)="openCreate()">Ajouter</soft-button>
      }
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <div class="soft-actions">
            @if (canUpdate(row)) {
              <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            }
            @if (canDelete(row)) {
              <soft-button size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
            }
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
  private readonly auth = inject(AuthService);
  private readonly session = inject(SessionStore);
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

  canCreate(): boolean {
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) {
      return this.session.hasRight(Droit.Databases);
    }
    return perms.some((p) => p.startsWith('SAGE:') && p.endsWith(':CREATE')) || perms.includes('SAGE:CREATE');
  }

  canRead(nomBd?: string | null): boolean {
    if (!nomBd) return true;
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) {
      return this.session.hasRight(Droit.Databases);
    }
    return perms.includes(`SAGE:${nomBd}:READ`);
  }

  canUpdate(row: Record<string, unknown>): boolean {
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) {
      return this.session.hasRight(Droit.Databases);
    }
    const nom = (row['nomBd'] as string) || '';
    return perms.includes(`SAGE:${nom}:UPDATE`);
  }

  canDelete(row: Record<string, unknown>): boolean {
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) {
      return this.session.hasRight(Droit.Databases);
    }
    const nom = (row['nomBd'] as string) || '';
    return perms.includes(`SAGE:${nom}:DELETE`);
  }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (items) => {
        const visible = items.filter((d) => this.canRead(d.nomBd));
        this.rows.set(visible.map(asRow));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    if (!this.canCreate()) {
      this.toast.error("Vous n'avez pas la permission d'ajouter une base SAGE.");
      return;
    }
    this.editId.set(null);
    this.form.set({ sqlAuth: true, typeBase: 'STANDARD' });
    this.modal.set(true);
  }

  openEdit(row: Record<string, unknown>): void {
    if (!this.canUpdate(row)) {
      this.toast.error("Vous n'avez pas la permission de modifier cette base SAGE.");
      return;
    }
    this.editId.set(row['id'] as number);
    this.form.set({ ...(row as unknown as SageDb) });
    this.modal.set(true);
  }

  closeModal(): void {
    this.modal.set(false);
  }

  onSaved(dto: Record<string, unknown>): void {
    const id = this.editId();
    if (id && !this.canUpdate(dto)) {
      this.toast.error("Vous n'avez pas la permission de modifier cette base SAGE.");
      return;
    }
    if (!id && !this.canCreate()) {
      this.toast.error("Vous n'avez pas la permission d'ajouter une base SAGE.");
      return;
    }
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
            : "Base enregistrée. Pour qu'elle apparaisse dans le menu en haut, accordez l'accès dans « Accès bases ».",
        );
        this.auth.refreshMe().subscribe();
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        this.toast.error(e?.error?.title || e?.error || 'Enregistrement impossible.');
      },
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!this.canDelete(row)) {
      this.toast.error("Vous n'avez pas la permission de supprimer cette base SAGE.");
      return;
    }
    if (!(await this.confirm.ask('Supprimer cette base SAGE ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Supprimé.');
        this.auth.refreshMe().subscribe();
        this.load();
      },
    });
  }
}
