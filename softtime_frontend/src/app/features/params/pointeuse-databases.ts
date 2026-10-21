import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { PointeuseDb, PageChangeEvent } from '../../shared/models';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
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
  selector: 'app-pointeuse-databases',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftButton, SoftCard, DataTable, SourceMappingStepper],
  template: `
    <page-header title="Bases pointeuse" subtitle="Connexions aux bases pointeuse (standard ou autre)">
      @if (canCreate()) {
        <soft-button (click)="openCreate()">Ajouter</soft-button>
      }
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()"
        [serverSide]="true" [total]="total()" [serverPage]="page()" [serverPageSize]="pageSize"
        (pageChange)="onPageChange($event)">
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
      systemType="POINTEUSE"
      [title]="editId() ? 'Modifier une base pointeuse' : 'Nouvelle base pointeuse'"
      [initial]="form()"
      [saving]="saving()"
      (closed)="closeModal()"
      (saved)="onSaved($event)"
    />
  `,
})
export class PointeuseDatabasesPage implements OnInit {
  private readonly svc = inject(PointeuseDatabasesService);
  private readonly session = inject(SessionStore);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  pageSize = 50;
  readonly total = signal(0);
  private requestSeq = 0;
  readonly form = signal<Partial<PointeuseDb>>({});
  readonly columns: Column[] = [
    { key: 'id', label: 'ID' },
    { key: 'serveur', label: 'Serveur' },
    { key: 'nomBd', label: 'Base' },
    { key: 'typeBase', label: 'Type' },
    { key: 'active', label: 'Active', format: (v) => (v ? 'Oui' : 'Non') },
  ];

  ngOnInit(): void {
    this.loadPage();
  }

  canCreate(): boolean {
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) {
      return this.session.hasRight(Droit.Databases);
    }
    return perms.some((p) => p.startsWith('POINTEUSE:') && p.endsWith(':CREATE')) || perms.includes('POINTEUSE:CREATE');
  }

  canUpdate(row: Record<string, unknown>): boolean {
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) {
      return this.session.hasRight(Droit.Databases);
    }
    const nom = (row['nomBd'] as string) || '';
    return perms.includes(`POINTEUSE:${nom}:UPDATE`);
  }

  canDelete(row: Record<string, unknown>): boolean {
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) {
      return this.session.hasRight(Droit.Databases);
    }
    const nom = (row['nomBd'] as string) || '';
    return perms.includes(`POINTEUSE:${nom}:DELETE`);
  }

  loadPage(): void {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged(this.page(), this.pageSize).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        this.rows.set(r.items.map(asRow));
        this.total.set(r.total);
        this.page.set(r.page);
        this.loading.set(false);
      },
      error: () => { if (seq === this.requestSeq) this.loading.set(false); },
    });
  }

  onPageChange(event: PageChangeEvent): void {
    this.page.set(event.page);
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  openCreate(): void {
    if (!this.canCreate()) {
      this.toast.error("Vous n'avez pas la permission d'ajouter une base pointeuse.");
      return;
    }
    this.editId.set(null);
    this.form.set({ sqlAuth: true, active: true, typeBase: 'STANDARD' });
    this.modal.set(true);
  }

  openEdit(row: Record<string, unknown>): void {
    if (!this.canUpdate(row)) {
      this.toast.error("Vous n'avez pas la permission de modifier cette base pointeuse.");
      return;
    }
    this.editId.set(row['id'] as number);
    this.form.set({ ...(row as unknown as PointeuseDb) });
    this.modal.set(true);
  }

  closeModal(): void {
    this.modal.set(false);
  }

  onSaved(dto: Record<string, unknown>): void {
    const id = this.editId();
    if (id && !this.canUpdate(dto)) {
      this.toast.error("Vous n'avez pas la permission de modifier cette base pointeuse.");
      return;
    }
    if (!id && !this.canCreate()) {
      this.toast.error("Vous n'avez pas la permission d'ajouter une base pointeuse.");
      return;
    }
    const payload = dto as unknown as PointeuseDb;
    this.saving.set(true);
    const req = id ? this.svc.update(id, payload) : this.svc.create({ ...payload, id: 0 });
    req.subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Enregistré.');
        this.svc.refresh().subscribe();
        this.loadPage();
      },
      error: (e) => {
        this.saving.set(false);
        this.toast.error(e?.error?.title || e?.error || 'Enregistrement impossible.');
      },
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!this.canDelete(row)) {
      this.toast.error("Vous n'avez pas la permission de supprimer cette base pointeuse.");
      return;
    }
    if (!(await this.confirm.ask('Supprimer cette base pointeuse ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Supprimé.');
        this.svc.refresh().subscribe();
        this.loadPage();
      },
    });
  }
}
