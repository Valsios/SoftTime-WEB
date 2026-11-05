import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { Role, PageChangeEvent } from '../../shared/models';
import { RolesService } from '../../shared/services/users.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-roles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput],
  template: `
    <page-header title="Rôles" subtitle="Gestion ROLE">
      <soft-button *ngIf="canCreate()" (click)="openCreate()">Ajouter</soft-button>
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()"
        [serverSide]="true" [total]="total()" [serverPage]="page()" [serverPageSize]="pageSize"
        [(searchTerm)]="tableSearch" (searchSubmit)="onTableSearch($event)"
        (pageChange)="onPageChange($event)">
        <ng-template #actions let-row>
          <div class="soft-actions" style="justify-content: end">
            <soft-button *ngIf="canUpdate()" size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            <soft-button *ngIf="canDelete()" size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
          </div>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier rôle' : 'Nouveau rôle'" (closed)="modal.set(false)">
      <soft-input label="Nom" [(ngModel)]="form.nom" name="nom"></soft-input>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
})
export class RolesPage implements OnInit {
  private readonly svc = inject(RolesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  readonly canCreate = computed(() => this.session.hasPermission('PARAMS:ROLES:CREATE', Droit.Parameters));
  readonly canUpdate = computed(() => this.session.hasPermission('PARAMS:ROLES:UPDATE', Droit.Parameters));
  readonly canDelete = computed(() => this.session.hasPermission('PARAMS:ROLES:DELETE', Droit.Parameters));

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  pageSize = 50;
  readonly total = signal(0);
  readonly tableSearch = signal('');
  private requestSeq = 0;
  form: Partial<Role> = {};
  readonly columns: Column[] = [{ key: 'nom', label: 'Nom' }];

  ngOnInit(): void { this.loadPage(); }
  loadPage(): void {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged(this.page(), this.pageSize, this.tableSearch()).subscribe({
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

  onTableSearch(term: string): void {
    this.tableSearch.set(term);
    this.page.set(1);
    this.loadPage();
  }
  openCreate(): void { this.editId.set(null); this.form = {}; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as Role) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as Role;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.loadPage(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer ce rôle ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.loadPage(); } });
  }
}
