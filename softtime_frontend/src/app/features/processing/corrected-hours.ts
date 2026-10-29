import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { CorrectedHour, PeriodRequest, PageChangeEvent } from '../../shared/models';
import { CorrectedHoursService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, ImportTemplatesModal, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';
import { toastImportResult } from '../../shared/utils/import-result';

@Component({
  selector: 'app-corrected-hours',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, PeriodFilter, ImportTemplatesModal],
  template: `
    <page-header title="Heures corrigées" subtitle="Corrections manuelles">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button *ngIf="canCreate()" (click)="openCreate()">Ajouter</soft-button>
      <label class="file-btn">
        <input #corrFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="secondary" (click)="corrFile.click()">Import Excel</soft-button>
      </label>
      <soft-button variant="ghost" (click)="templatesOpen.set(true)">Modèles d'import</soft-button>
    </page-header>

    <period-filter (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [serverSide]="true"
        [total]="total()"
        [serverPageSize]="pageSize"
        [serverPage]="page()"
        [(searchTerm)]="tableSearch"
        (searchSubmit)="onTableSearch($event)"
        (pageChange)="onPageChange($event)"
      >
        <ng-template #actions let-row>
          <div class="soft-actions">
            <soft-button *ngIf="canUpdate()" size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            <soft-button *ngIf="canDelete()" size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
          </div>
        </ng-template>
      </data-table>
    </soft-card>

    <soft-modal [open]="modal()" title="Correction" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Matricule" [(ngModel)]="form.matricule" name="matricule"></soft-input>
        <soft-input label="Date entrée" type="date" [(ngModel)]="form.dateIn" name="dateIn"></soft-input>
        <soft-input label="Date sortie" type="date" [(ngModel)]="form.dateOut" name="dateOut"></soft-input>
        <soft-input label="H. entrée" type="time" [(ngModel)]="form.hEntree" name="hEntree"></soft-input>
        <soft-input label="H. sortie" type="time" [(ngModel)]="form.hSortie" name="hSortie"></soft-input>
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
export class CorrectedHoursPage {
  private readonly svc = inject(CorrectedHoursService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  readonly canCreate = computed(() => this.session.hasPermission('PROC:CORRECTED_HOURS:CREATE', Droit.Processing));
  readonly canUpdate = computed(() => this.session.hasPermission('PROC:CORRECTED_HOURS:UPDATE', Droit.Processing));
  readonly canDelete = computed(() => this.session.hasPermission('PROC:CORRECTED_HOURS:DELETE', Droit.Processing));

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly templatesOpen = signal(false);
  readonly importKeys = ['corrections'];
  readonly editId = signal<number | null>(null);
  pageSize = 50;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly tableSearch = signal('');
  form: Partial<CorrectedHour> = {};
  private lastFilter: PeriodRequest | null = null;
  private requestSeq = 0;

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateIn', label: 'Entrée', format: fmtDate },
    { key: 'dateOut', label: 'Sortie', format: fmtDate },
    { key: 'hEntree', label: 'H. entrée' },
    { key: 'hSortie', label: 'H. sortie' },
    { key: 'retard', label: 'Retard' },
    { key: 'hs', label: 'HS' },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
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

  private loadPage(): void {
    if (!this.lastFilter) return;
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged({
      ...this.lastFilter,
      search: this.tableSearch(),
      page: this.page(),
      pageSize: this.pageSize,
    }).subscribe({
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

  private reload(): void {
    if (this.lastFilter) this.loadPage();
  }

  openCreate(): void { this.editId.set(null); this.form = {}; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    this.form = {
      ...(row as unknown as CorrectedHour),
      dateIn: fmtDate(row['dateIn']),
      dateOut: fmtDate(row['dateOut']),
    };
    this.modal.set(true);
  }

  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as CorrectedHour;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Enregistré.');
        this.reload();
      },
      error: () => this.saving.set(false),
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette correction ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Supprimé.');
        this.reload();
      },
    });
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
          `${r.imported} correction(s) importée(s), ${r.skipped} ligne(s) ignorée(s).`,
        );
        this.reload();
      },
    });
    input.value = '';
  }

  exportExcel(): void {
    if (!this.lastFilter) {
      this.excel.download('heures-corrigees', this.columns, this.rows(), 'Données', this.tableSearch());
      return;
    }
    this.loading.set(true);
    this.svc.list(this.lastFilter).subscribe({
      next: (items) => {
        this.loading.set(false);
        this.excel.download('heures-corrigees', this.columns, items.map(asRow), 'Données', this.tableSearch());
      },
      error: () => this.loading.set(false),
    });
  }
}
