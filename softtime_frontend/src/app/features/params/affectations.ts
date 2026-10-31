import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, of } from 'rxjs';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { Affectation, CardPaie, Category, PageChangeEvent } from '../../shared/models';
import { AffectationsService, CardPaieService, CategoriesService } from '../../shared/services/catalog.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, ImportTemplatesModal, PageHeader, SoftButton, SoftCard, SoftModal, SoftSelect } from '../../shared/components';
import { asRow } from '../../shared/utils/date';
import { toastImportResult } from '../../shared/utils/import-result';
import { cardEmployeeLabel } from '../../shared/utils/labels';

@Component({
  selector: 'app-affectations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftSelect, ImportTemplatesModal],
  template: `
    <page-header title="Affectations" subtitle="Salariés par catégorie">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <label class="file-btn">
        <input #affFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="secondary" (click)="affFile.click()">Import Excel</soft-button>
      </label>
      <soft-button variant="ghost" (click)="templatesOpen.set(true)">Modèles d'import</soft-button>
      <soft-button *ngIf="canCreate()" (click)="openCreate()">Ajouter</soft-button>
    </page-header>

    <div class="soft-toolbar">
      <soft-select
        label="Filtrer par catégorie"
        [(ngModel)]="filterCategoryId"
        name="filterCat"
        placeholder="Toutes"
        [options]="categoryOptions()"
        (ngModelChange)="onFilterChange()"
      ></soft-select>

      <soft-select
        label="Département"
        [(ngModel)]="filterDept"
        name="filterDept"
        [options]="deptOptions()"
        (ngModelChange)="onFilterChange()"
      ></soft-select>

      <soft-select
        label="Service"
        [(ngModel)]="filterService"
        name="filterServ"
        [options]="servOptions()"
        (ngModelChange)="onFilterChange()"
      ></soft-select>
    </div>

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

    <soft-modal [open]="modal()" title="Affectation" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-select
          label="Salarié"
          [required]="true"
          [(ngModel)]="form.cardPaieId"
          name="cardPaieId"
          placeholder="Sélectionner un salarié"
          [options]="cardOptions()"
          [hint]="cardOptions().length ? '' : 'Aucun salarié disponible : vérifiez les correspondances paie.'"
        ></soft-select>
        <soft-select label="Catégorie" [(ngModel)]="form.categoryId" name="categoryId" [options]="categoryOptions()"></soft-select>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
    <app-import-templates-modal
      [open]="templatesOpen()"
      [keys]="importKeys"
      (closed)="templatesOpen.set(false)"
    />
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .file-btn { cursor: pointer; }`],
})
export class AffectationsPage implements OnInit {
  private readonly svc = inject(AffectationsService);
  private readonly catSvc = inject(CategoriesService);
  private readonly cards = inject(CardPaieService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  readonly canCreate = computed(() => this.session.hasPermission('PARAMS:AFFECTATIONS:CREATE', Droit.Parameters));
  readonly canUpdate = computed(() => this.session.hasPermission('PARAMS:AFFECTATIONS:UPDATE', Droit.Parameters));
  readonly canDelete = computed(() => this.session.hasPermission('PARAMS:AFFECTATIONS:DELETE', Droit.Parameters));

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly templatesOpen = signal(false);
  readonly importKeys = ['affectations'];
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  pageSize = 50;
  readonly total = signal(0);
  readonly tableSearch = signal('');
  private requestSeq = 0;
  readonly categoryOptions = signal<{ value: number; label: string }[]>([]);
  readonly cardOptions = signal<{ value: number; label: string }[]>([]);
  private readonly categoryLabels = new Map<number, string>();
  readonly deptOptions = signal<{ value: string; label: string }[]>([{ value: 'ALL', label: 'Tous' }]);
  readonly servOptions = signal<{ value: string; label: string }[]>([{ value: 'ALL', label: 'Tous' }]);

  filterCategoryId: number | null = null;
  filterDept = 'ALL';
  filterService = 'ALL';
  form: Partial<Affectation> = {};

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'nom', label: 'Nom' },
    { key: 'prenom', label: 'Prénom' },
    { key: 'departement', label: 'Département' },
    { key: 'service', label: 'Service' },
    { key: 'categoryName', label: 'Catégorie' },
  ];

  ngOnInit(): void {
    this.svc.options().subscribe({
      next: (options) => {
        this.deptOptions.set([
          { value: 'ALL', label: 'Tous' },
          ...options.departements.map((d) => ({ value: d, label: d })),
        ]);
        this.servOptions.set([
          { value: 'ALL', label: 'Tous' },
          ...options.services.map((s) => ({ value: s, label: s })),
        ]);
      },
    });

    forkJoin({
      categories: this.catSvc.list().pipe(catchError(() => of<Category[]>([]))),
      cards: this.cards.list().pipe(catchError(() => of<CardPaie[]>([]))),
    }).subscribe(({ categories, cards }) => {
      const categoryEntries = categories.map((category) => ({
        id: category.id,
        label: category.intitule ?? `Catégorie #${category.id}`,
      }));
      this.categoryOptions.set(categoryEntries.map((entry) => ({ value: entry.id, label: entry.label })));
      for (const entry of categoryEntries) this.categoryLabels.set(entry.id, entry.label);

      const cardEntries = cards
        .map((card) => ({ id: card.id, label: cardEmployeeLabel(card) }))
        .sort((a, b) => a.label.localeCompare(b.label, 'fr', { numeric: true }));
      this.cardOptions.set(cardEntries.map((entry) => ({ value: entry.id, label: entry.label })));

      this.loadPage();
    });
  }

  loadPage(): void {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged({
      categoryId: this.filterCategoryId,
      departement: this.filterDept === 'ALL' ? null : this.filterDept,
      service: this.filterService === 'ALL' ? null : this.filterService,
      search: this.tableSearch(),
      page: this.page(),
      pageSize: this.pageSize,
    }).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        this.rows.set(
          r.items.map((affectation) =>
            asRow({
              ...affectation,
              categoryName: this.categoryLabels.get(affectation.categoryId) ?? '—',
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

  onFilterChange(): void {
    this.page.set(1);
    this.loadPage();
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

  openCreate(): void { this.editId.set(null); this.form = { cardPaieId: 0, categoryId: this.categoryOptions()[0]?.value ?? 0 }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void {
    this.ensureCardOption(row['cardPaieId'] as number);
    this.ensureCategoryOption(row['categoryId'] as number);
    this.editId.set(row['id'] as number);
    this.form = { cardPaieId: row['cardPaieId'] as number, categoryId: row['categoryId'] as number, id: row['id'] as number };
    this.modal.set(true);
  }
  save(): void {
    if (!this.form.cardPaieId) {
      this.toast.error('Sélectionnez un salarié.');
      return;
    }
    if (!this.form.categoryId) {
      this.toast.error('Sélectionnez une catégorie.');
      return;
    }
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as Affectation;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.loadPage(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette affectation ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.loadPage(); } });
  }
  exportExcel(): void {
    this.svc.listAll({
      categoryId: this.filterCategoryId,
      departement: this.filterDept === 'ALL' ? null : this.filterDept,
      service: this.filterService === 'ALL' ? null : this.filterService,
    }).subscribe({
      next: (rows) => this.excel.download(
        'affectations',
        this.columns,
        rows.map((affectation) =>
          asRow({
            ...affectation,
            categoryName: this.categoryLabels.get(affectation.categoryId) ?? '—',
          }),
        ),
        'Données',
        this.tableSearch(),
      ),
    });
  }

  /** Conserve les valeurs courantes même si la carte ou la catégorie n'est plus dans la liste chargée. */
  private ensureCardOption(id: number | null | undefined): void {
    if (!id || this.cardOptions().some((option) => Number(option.value) === id)) return;
    this.cardOptions.update((options) => [{ value: id, label: 'Salarié introuvable' }, ...options]);
  }

  private ensureCategoryOption(id: number | null | undefined): void {
    if (!id || this.categoryOptions().some((option) => Number(option.value) === id)) return;
    this.categoryOptions.update((options) => [{ value: id, label: 'Catégorie introuvable' }, ...options]);
  }
  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => {
        toastImportResult(
          this.toast,
          r,
          `${r.imported} affectation(s) importée(s), ${r.skipped} ligne(s) ignorée(s).`,
        );
        this.loadPage();
      },
    });
    input.value = '';
  }
}