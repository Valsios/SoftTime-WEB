import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { Category, PageChangeEvent } from '../../shared/models';
import { CardPaieService, CategoriesService } from '../../shared/services/catalog.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect } from '../../shared/components';
import { asRow } from '../../shared/utils/date';
import { cardEmployeeLabel } from '../../shared/utils/labels';

@Component({
  selector: 'app-categories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect],
  template: `
    <page-header title="Catégories" subtitle="Catégories salariés">
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
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier catégorie' : 'Nouvelle catégorie'" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Intitulé" [(ngModel)]="form.intitule" name="intitule"></soft-input>
        <soft-select
          label="Superviseur"
          [required]="true"
          [(ngModel)]="form.supervisorCardId"
          name="supervisorCardId"
          placeholder="Sélectionner un superviseur"
          [options]="cardOptions()"
          [hint]="cardOptions().length ? '' : 'Aucun salarié disponible : vérifiez les correspondances paie.'"
        ></soft-select>
        <soft-input label="Pause" type="time" [(ngModel)]="form.pause" name="pause"></soft-input>
        <soft-input label="Heures / semaine" type="number" [(ngModel)]="form.heuresSemaine" name="heuresSemaine"></soft-input>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; }`],
})
export class CategoriesPage implements OnInit {
  private readonly svc = inject(CategoriesService);
  private readonly cards = inject(CardPaieService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  readonly canCreate = computed(() => this.session.hasPermission('PARAMS:CATEGORIES:CREATE', Droit.Parameters));
  readonly canUpdate = computed(() => this.session.hasPermission('PARAMS:CATEGORIES:UPDATE', Droit.Parameters));
  readonly canDelete = computed(() => this.session.hasPermission('PARAMS:CATEGORIES:DELETE', Droit.Parameters));

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  pageSize = 50;
  readonly total = signal(0);
  readonly tableSearch = signal('');
  readonly cardOptions = signal<{ value: number; label: string }[]>([]);
  private readonly cardLabels = new Map<number, string>();
  private requestSeq = 0;
  form: Partial<Category> = {};
  readonly columns: Column[] = [
    { key: 'intitule', label: 'Intitulé' },
    { key: 'supervisorName', label: 'Superviseur' },
    { key: 'pause', label: 'Pause' },
    { key: 'heuresSemaine', label: 'H/sem.' },
  ];

  ngOnInit(): void {
    this.cards.list().subscribe({
      next: (list) => {
        const entries = list
          .map((card) => ({ id: card.id, label: cardEmployeeLabel(card) }))
          .sort((a, b) => a.label.localeCompare(b.label, 'fr', { numeric: true }));
        this.cardOptions.set(entries.map((entry) => ({ value: entry.id, label: entry.label })));
        for (const entry of entries) this.cardLabels.set(entry.id, entry.label);
        this.loadPage();
      },
      error: () => this.loadPage(),
    });
  }

  loadPage(): void {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged(this.page(), this.pageSize, this.tableSearch()).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        this.rows.set(
          r.items.map((category) =>
            asRow({
              ...category,
              supervisorName: this.cardLabels.get(category.supervisorCardId) ?? '—',
            }),
          ),
        );
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
  openCreate(): void { this.editId.set(null); this.form = { supervisorCardId: 0 }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void {
    this.ensureSupervisorOption(row['supervisorCardId'] as number);
    this.editId.set(row['id'] as number);
    this.form = {
      id: row['id'] as number,
      intitule: (row['intitule'] as string | null) ?? null,
      supervisorCardId: row['supervisorCardId'] as number,
      pause: (row['pause'] as string | null) ?? null,
      heuresSemaine: (row['heuresSemaine'] as number | null) ?? null,
    };
    this.modal.set(true);
  }
  save(): void {
    if (!this.form.supervisorCardId) {
      this.toast.error('Sélectionnez un superviseur.');
      return;
    }
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as Category;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.loadPage(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette catégorie ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.loadPage(); } });
  }

  /** Conserve la valeur courante même si la carte n'est plus dans la liste chargée. */
  private ensureSupervisorOption(id: number | null | undefined): void {
    if (!id || this.cardOptions().some((option) => Number(option.value) === id)) return;
    this.cardOptions.update((options) => [{ value: id, label: 'Salarié introuvable' }, ...options]);
  }
}
