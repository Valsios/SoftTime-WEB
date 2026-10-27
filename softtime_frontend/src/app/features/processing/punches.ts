import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PunchUpdate, PeriodRequest, PageChangeEvent } from '../../shared/models';
import { PunchesService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-punches',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftInput, SoftModal, PeriodFilter],
  template: `
    <page-header title="Pointages" subtitle="Liste, import et traitement des pointages">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="secondary" (click)="importClock()">Import pointeuse</soft-button>
      <soft-button [loading]="processing()" (click)="preprocess()">Traiter les pointages</soft-button>
      <soft-button variant="danger" [loading]="busy()" (click)="purge()">Purger la période</soft-button>
      <label class="file-btn">
        <input #punchFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="accent" (click)="punchFile.click()">Import Excel</soft-button>
      </label>
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
        (pageChange)="onPageChange($event)"
      >
        <ng-template #actions let-row>
          <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
        </ng-template>
      </data-table>
    </soft-card>

    <soft-modal [open]="modal()" title="Corriger le pointage" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Matricule" [(ngModel)]="currentMatricule" name="matricule" [disabled]="true"></soft-input>
        <soft-input label="Date" type="date" [(ngModel)]="form.date" name="date"></soft-input>
        <soft-input label="Heure" type="time" [(ngModel)]="form.heure" name="heure"></soft-input>
        <soft-input label="Type" [(ngModel)]="form.type" name="type"></soft-input>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="busy()" (click)="saveEdit()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.file-btn { cursor: pointer; } .form-grid { display: grid; gap: 14px; }`],
})
export class PunchesPage {
  private readonly svc = inject(PunchesService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(false);
  readonly processing = signal(false);
  readonly busy = signal(false);
  readonly modal = signal(false);
  pageSize = 50;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  private lastFilter: PeriodRequest | null = null;
  private requestSeq = 0;
  private editId: number | null = null;
  currentMatricule = '';
  form: PunchUpdate = {};

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'date', label: 'Date', format: fmtDate },
    { key: 'heure', label: 'Heure' },
    { key: 'type', label: 'Type' },
    { key: 'importDate', label: 'Import', format: fmtDate },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.page.set(1);
    this.loadPage();
  }

  /** Charge la page courante (liste paginée : une période peut compter des dizaines de milliers de lignes). */
  private loadPage(): void {
    if (!this.lastFilter) return;
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged({
      ...this.lastFilter,
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

  onPageChange(event: PageChangeEvent): void {
    this.page.set(event.page);
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  importClock(): void {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    this.svc.importClock({
      from: this.lastFilter.from,
      to: this.lastFilter.to,
      matriculeFrom: this.lastFilter.matriculeFrom,
      matriculeTo: this.lastFilter.matriculeTo,
    }).subscribe({
      next: (r) => {
        this.toast.success(r.message || `${r.imported} importé(s).`);
        // Le desktop enchaîne import et pré-traitement : on reproduit ce parcours.
        this.runPreprocess();
      },
    });
  }

  /** Pré-traitement : transforme les pointages en heures corrigées + anomalies à arbitrer. */
  preprocess(): void {
    this.runPreprocess();
  }

  private runPreprocess(): void {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    this.processing.set(true);
    this.svc.preprocess(this.lastFilter).subscribe({
      next: (r) => {
        this.processing.set(false);
        this.toast.success(r.message || `${r.correctionsCreees} correction(s) créée(s).`);
        this.onSearch(this.lastFilter!);
      },
      error: () => { this.processing.set(false); this.refresh(); },
    });
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => {
        this.toast.success(r.message || `${r.imported} importé(s).`);
        if (this.lastFilter) this.runPreprocess();
        else this.refresh();
      },
    });
    input.value = '';
  }
  /**
   * Export de la grille complète : la pagination ne porte que sur l'affichage, l'export recharge
   * donc toute la période pour ne pas tronquer le fichier.
   */
  exportExcel(): void {
    if (!this.lastFilter) {
      this.excel.download('pointages', this.columns, this.rows());
      return;
    }
    this.loading.set(true);
    this.svc.list(this.lastFilter).subscribe({
      next: (items) => {
        this.loading.set(false);
        this.excel.download('pointages', this.columns, items.map(asRow));
      },
      error: () => this.loading.set(false),
    });
  }

  openEdit(row: Record<string, unknown>): void {
    this.editId = row['id'] as number;
    this.currentMatricule = (row['matricule'] as string) ?? '';
    this.form = {
      date: this.toDateInput(row['date']),
      heure: this.toTimeInput(row['heure']),
      type: (row['type'] as string) ?? '',
    };
    this.modal.set(true);
  }

  saveEdit(): void {
    if (this.editId == null) return;
    this.busy.set(true);
    // Les valeurs vides ne sont pas transmises : le champ correspondant reste inchangé.
    const payload: PunchUpdate = {
      date: this.form.date || undefined,
      heure: this.form.heure || undefined,
      type: this.form.type ?? '',
    };
    this.svc.update(this.editId, payload).subscribe({
      next: () => {
        this.busy.set(false);
        this.modal.set(false);
        this.toast.success('Pointage corrigé. Relancez le traitement pour propager la correction.');
        this.refresh();
      },
      error: () => this.busy.set(false),
    });
  }

  /** Purge la période : pointages + corrections automatiques + anomalies (archivés). */
  async purge(): Promise<void> {
    if (!this.lastFilter) { this.toast.warning('Lancez d\'abord une recherche.'); return; }
    if (!(await this.confirm.ask(
      'Purger les pointages de la période ? Les corrections automatiques et les anomalies seront aussi retirées. L\'opération est archivée et consultable dans Historique.'))) return;
    this.busy.set(true);
    this.svc.purge(this.lastFilter).subscribe({
      next: (r) => { this.busy.set(false); this.toast.success(r.message); this.refresh(); },
      error: () => this.busy.set(false),
    });
  }

  private refresh(): void {
    if (this.lastFilter) this.loadPage();
  }

  private toDateInput(value: unknown): string | null {
    return typeof value === 'string' && value.length >= 10 ? value.slice(0, 10) : null;
  }

  private toTimeInput(value: unknown): string | null {
    return typeof value === 'string' && value.length >= 5 ? value.slice(0, 5) : null;
  }
}
