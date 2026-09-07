import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { Tolerance } from '../../shared/models';
import { CategoriesService, TolerancesService } from '../../shared/services/catalog.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect, SoftToggle } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-tolerances',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect, SoftToggle],
  template: `
    <page-header title="Tolérances" subtitle="Tolérances entrée/sortie par catégorie">
      <soft-button (click)="openCreate()">Ajouter</soft-button>
    </page-header>

    <div class="soft-toolbar">
      <soft-select label="Catégorie" [(ngModel)]="filterCategoryId" name="filterCat" placeholder="Toutes" [options]="categoryOptions()" (ngModelChange)="load()"></soft-select>
    </div>

    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
        </ng-template>
      </data-table>
    </soft-card>

    <soft-modal [open]="modal()" title="Tolérance" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-select label="Catégorie" [(ngModel)]="form.categoryId" name="categoryId" [options]="categoryOptions()"></soft-select>
        <soft-input label="Tolérance (min)" type="number" [(ngModel)]="form.tolerance" name="tolerance"></soft-input>
        <soft-input label="Sortie" type="number" [(ngModel)]="form.sortie" name="sortie"></soft-input>
        <label class="toggle-row"><span>Type sortie</span><soft-toggle [(ngModel)]="form.typesTol" name="typesTol"></soft-toggle></label>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .toggle-row { display:flex; justify-content:space-between; align-items:center; font-weight:600; }`],
})
export class TolerancesPage implements OnInit {
  private readonly svc = inject(TolerancesService);
  private readonly catSvc = inject(CategoriesService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly categoryOptions = signal<{ value: number; label: string }[]>([]);
  filterCategoryId: number | null = null;
  form: Partial<Tolerance> = {};

  readonly columns: Column[] = [
    { key: 'categoryId', label: 'Catégorie' },
    { key: 'tolerance', label: 'Tolérance' },
    { key: 'sortie', label: 'Sortie' },
    { key: 'typesTol', label: 'Type sortie', format: (v) => (v ? 'Oui' : 'Non') },
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

  openCreate(): void { this.editId.set(null); this.form = { categoryId: this.categoryOptions()[0]?.value ?? 0, tolerance: 0, typesTol: false }; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as Tolerance) }; this.modal.set(true); }

  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as Tolerance;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.load(); }, error: () => this.saving.set(false) });
  }
}
