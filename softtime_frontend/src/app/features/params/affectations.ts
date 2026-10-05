import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { Affectation, CardPaie, Category, DepartementService } from '../../shared/models';
import { AffectationsService, CardPaieService, CategoriesService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

interface AffectationRow extends Affectation {
  matricule?: string | null;
  nom?: string | null;
  prenom?: string | null;
  departement?: string | null;
  service?: string | null;
}

@Component({
  selector: 'app-affectations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect],
  template: `
    <page-header title="Affectations" subtitle="Salariés par catégorie">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <label class="file-btn">
        <input #affFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="secondary" (click)="affFile.click()">Import Excel</soft-button>
      </label>
      <soft-button (click)="openCreate()">Ajouter</soft-button>
    </page-header>

    <div class="soft-toolbar">
      <soft-select
        label="Filtrer par catégorie"
        [(ngModel)]="filterCategoryId"
        name="filterCat"
        placeholder="Toutes"
        [options]="categoryOptions()"
        (ngModelChange)="load()"
      ></soft-select>

      <soft-select
        label="Département"
        [(ngModel)]="filterDept"
        name="filterDept"
        [options]="deptOptions()"
        (ngModelChange)="applyFilters()"
      ></soft-select>

      <soft-select
        label="Service"
        [(ngModel)]="filterService"
        name="filterServ"
        [options]="servOptions()"
        (ngModelChange)="applyFilters()"
      ></soft-select>
    </div>

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

    <soft-modal [open]="modal()" title="Affectation" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Card paie ID" type="number" [(ngModel)]="form.cardPaieId" name="cardPaieId"></soft-input>
        <soft-select label="Catégorie" [(ngModel)]="form.categoryId" name="categoryId" [options]="categoryOptions()"></soft-select>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .file-btn { cursor: pointer; }`],
})
export class AffectationsPage implements OnInit {
  private readonly svc = inject(AffectationsService);
  private readonly catSvc = inject(CategoriesService);
  private readonly cardSvc = inject(CardPaieService);
  private readonly deptSvc = inject(DepartementServiceService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly categoryOptions = signal<{ value: number; label: string }[]>([]);
  readonly deptOptions = signal<{ value: string; label: string }[]>([{ value: 'ALL', label: 'Tous' }]);
  readonly servOptions = signal<{ value: string; label: string }[]>([{ value: 'ALL', label: 'Tous' }]);

  filterCategoryId: number | null = null;
  filterDept = 'ALL';
  filterService = 'ALL';
  form: Partial<Affectation> = {};

  private cardsById = new Map<number, CardPaie>();
  private enrichedRows: AffectationRow[] = [];

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'nom', label: 'Nom' },
    { key: 'prenom', label: 'Prénom' },
    { key: 'departement', label: 'Département' },
    { key: 'service', label: 'Service' },
    { key: 'categoryId', label: 'Catégorie' },
  ];

  ngOnInit(): void {
    this.catSvc.list().subscribe((cats) =>
      this.categoryOptions.set(cats.map((c) => ({ value: c.id, label: c.intitule ?? `Cat. ${c.id}` }))),
    );
    this.cardSvc.list().subscribe((cards) => {
      this.cardsById = new Map(cards.map((c) => [c.id, c]));
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.svc.list(this.filterCategoryId ?? undefined).subscribe({
      next: (items) => {
        const enriched: AffectationRow[] = items.map((a) => {
          const card = this.cardsById.get(a.cardPaieId);
          return {
            ...a,
            matricule: card?.sageMatricule?.trim() ?? null,
            nom: card?.sageNom ?? null,
            prenom: card?.sagePrenom ?? null,
          };
        });
        this.loadDepartements(enriched);
      },
      error: () => this.loading.set(false),
    });
  }

  private loadDepartements(items: AffectationRow[]): void {
    const matricules = items.map((r) => r.matricule).filter((m): m is string => !!m);
    if (matricules.length === 0) {
      this.finish(items);
      return;
    }
    this.deptSvc.lookupBatch(matricules).subscribe({
      next: (results: DepartementService[]) => {
        const map = new Map(results.map((r) => [r.matricule.trim(), r]));
        const withDept = items.map((r) => {
          const ds = r.matricule ? map.get(r.matricule) : undefined;
          return { ...r, departement: ds?.departement ?? null, service: ds?.service ?? null };
        });
        this.finish(withDept);
      },
      error: () => this.finish(items),
    });
  }

  private finish(items: AffectationRow[]): void {
    this.enrichedRows = items;
    const depts = Array.from(new Set(items.map((r) => r.departement).filter((d): d is string => !!d))).sort();
    const servs = Array.from(new Set(items.map((r) => r.service).filter((s): s is string => !!s))).sort();
    this.deptOptions.set([{ value: 'ALL', label: 'Tous' }, ...depts.map((d) => ({ value: d, label: d }))]);
    this.servOptions.set([{ value: 'ALL', label: 'Tous' }, ...servs.map((s) => ({ value: s, label: s }))]);
    this.applyFilters();
    this.loading.set(false);
  }

  applyFilters(): void {
    const filtered = this.enrichedRows.filter(
      (r) =>
        (this.filterDept === 'ALL' || r.departement === this.filterDept) &&
        (this.filterService === 'ALL' || r.service === this.filterService),
    );
    this.rows.set(filtered.map(asRow));
  }

  openCreate(): void { this.editId.set(null); this.form = { cardPaieId: 0, categoryId: this.categoryOptions()[0]?.value ?? 0 }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { cardPaieId: row['cardPaieId'] as number, categoryId: row['categoryId'] as number, id: row['id'] as number }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as Affectation;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette affectation ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.load(); } });
  }
  exportExcel(): void {
    this.excel.download('affectations', this.columns, this.rows());
  }
  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => { this.toast.success(r.message || `${r.imported} importé(s).`); this.load(); },
    });
    input.value = '';
  }
}