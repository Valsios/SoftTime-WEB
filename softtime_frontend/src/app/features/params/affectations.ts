import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { Affectation, Category } from '../../shared/models';
import { AffectationsService, CategoriesService } from '../../shared/services/catalog.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

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
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly categoryOptions = signal<{ value: number; label: string }[]>([]);
  filterCategoryId: number | null = null;
  form: Partial<Affectation> = {};

  readonly columns: Column[] = [
    { key: 'id', label: 'ID' },
    { key: 'cardPaieId', label: 'Card paie' },
    { key: 'categoryId', label: 'Catégorie' },
  ];

  ngOnInit(): void {
    this.catSvc.list().subscribe((cats) =>
      this.categoryOptions.set(cats.map((c) => ({ value: c.id, label: c.intitule ?? `Cat. ${c.id}` }))),
    );
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.list(this.filterCategoryId ?? undefined).subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void { this.editId.set(null); this.form = { cardPaieId: 0, categoryId: this.categoryOptions()[0]?.value ?? 0 }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as Affectation) }; this.modal.set(true); }
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
